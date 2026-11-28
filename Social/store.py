"""Armazenamento da rede social (SQLite da biblioteca padrão).

Regras de privacidade que valem aqui:
- Nenhuma coordenada, endereço ou dado de localização é aceito ou guardado.
- Usuário é identificado por um token anônimo; o servidor guarda só o hash do token.
- Post pode ser "friends" (só amigos) ou "public" (aparece no feed público, sem dados do autor além do nome).
"""

import hashlib
import secrets
import sqlite3
import string
import threading
import time

SCHEMA = """
CREATE TABLE IF NOT EXISTS users (
    id TEXT PRIMARY KEY,
    token_hash TEXT NOT NULL UNIQUE,
    code TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL DEFAULT '',
    created_at INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS friendships (
    requester_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    addressee_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    status TEXT NOT NULL CHECK (status IN ('pending', 'accepted')),
    created_at INTEGER NOT NULL,
    PRIMARY KEY (requester_id, addressee_id)
);
CREATE TABLE IF NOT EXISTS posts (
    id TEXT PRIMARY KEY,
    author_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    caption TEXT NOT NULL DEFAULT '',
    visibility TEXT NOT NULL CHECK (visibility IN ('friends', 'public')),
    identification_points INTEGER NOT NULL DEFAULT 0,
    created_at INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS post_images (
    post_id TEXT NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
    position INTEGER NOT NULL,
    PRIMARY KEY (post_id, position)
);
CREATE TABLE IF NOT EXISTS reports (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    post_id TEXT NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
    reporter_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    reason TEXT NOT NULL,
    created_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS posts_by_time ON posts(created_at);
CREATE INDEX IF NOT EXISTS friendships_addressee ON friendships(addressee_id);
"""

# Código de amizade: sem letras e números parecidos (0/O, 1/I), para ser lido em voz alta.
CODE_ALPHABET = "".join(c for c in string.ascii_uppercase + "23456789" if c not in "IO")
CODE_LENGTH = 8


def now_ms() -> int:
    return int(time.time() * 1000)


def hash_token(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


class Store:
    def __init__(self, path: str):
        self.db = sqlite3.connect(path, check_same_thread=False)
        self.db.row_factory = sqlite3.Row
        self.db.execute("PRAGMA foreign_keys = ON")
        self.db.executescript(SCHEMA)
        self.lock = threading.RLock()

    # ---- usuários -------------------------------------------------------------------------------

    def create_user(self, token: str) -> dict:
        with self.lock, self.db:
            user_id = secrets.token_hex(16)
            while True:
                code = "".join(secrets.choice(CODE_ALPHABET) for _ in range(CODE_LENGTH))
                try:
                    self.db.execute(
                        "INSERT INTO users (id, token_hash, code, display_name, created_at) VALUES (?, ?, ?, '', ?)",
                        (user_id, hash_token(token), code, now_ms()),
                    )
                    return {"id": user_id, "code": code, "displayName": ""}
                except sqlite3.IntegrityError:
                    # Só repete o sorteio quando o código já existe; qualquer outro conflito é erro.
                    if self.db.execute("SELECT 1 FROM users WHERE code = ?", (code,)).fetchone() is None:
                        raise

    def user_by_token(self, token: str) -> dict | None:
        with self.lock:
            row = self.db.execute("SELECT * FROM users WHERE token_hash = ?", (hash_token(token),)).fetchone()
            return self._user(row)

    def user_by_id(self, user_id: str) -> dict | None:
        with self.lock:
            row = self.db.execute("SELECT * FROM users WHERE id = ?", (user_id,)).fetchone()
            return self._user(row)

    def user_by_code(self, code: str) -> dict | None:
        with self.lock:
            row = self.db.execute("SELECT * FROM users WHERE code = ?", (code.upper(),)).fetchone()
            return self._user(row)

    def set_display_name(self, user_id: str, name: str) -> None:
        with self.lock, self.db:
            self.db.execute("UPDATE users SET display_name = ? WHERE id = ?", (name, user_id))

    def delete_user(self, user_id: str) -> list[str]:
        """Apaga a conta e tudo que ela publicou (cascata). Devolve os ids dos posts para remover as fotos."""
        with self.lock, self.db:
            post_ids = [r["id"] for r in self.db.execute("SELECT id FROM posts WHERE author_id = ?", (user_id,))]
            self.db.execute("DELETE FROM users WHERE id = ?", (user_id,))
            return post_ids

    @staticmethod
    def _user(row) -> dict | None:
        if row is None:
            return None
        return {"id": row["id"], "code": row["code"], "displayName": row["display_name"]}

    # ---- amizades -------------------------------------------------------------------------------

    def request_friend(self, me: str, other: str) -> str:
        """Devolve 'pending', 'accepted' (se o outro já tinha pedido) ou 'already' (já são amigos)."""
        with self.lock, self.db:
            existing = self.db.execute(
                "SELECT requester_id, status FROM friendships WHERE "
                "(requester_id = ? AND addressee_id = ?) OR (requester_id = ? AND addressee_id = ?)",
                (me, other, other, me),
            ).fetchone()
            if existing is not None:
                if existing["status"] == "accepted":
                    return "already"
                if existing["requester_id"] == me:
                    return "pending"
                self.db.execute(
                    "UPDATE friendships SET status = 'accepted' WHERE requester_id = ? AND addressee_id = ?",
                    (other, me),
                )
                return "accepted"

            self.db.execute(
                "INSERT INTO friendships (requester_id, addressee_id, status, created_at) VALUES (?, ?, 'pending', ?)",
                (me, other, now_ms()),
            )
            return "pending"

    def accept_friend(self, me: str, other: str) -> bool:
        with self.lock, self.db:
            cursor = self.db.execute(
                "UPDATE friendships SET status = 'accepted' WHERE requester_id = ? AND addressee_id = ? AND status = 'pending'",
                (other, me),
            )
            return cursor.rowcount > 0

    def remove_friend(self, me: str, other: str) -> None:
        with self.lock, self.db:
            self.db.execute(
                "DELETE FROM friendships WHERE (requester_id = ? AND addressee_id = ?) OR (requester_id = ? AND addressee_id = ?)",
                (me, other, other, me),
            )

    def friends(self, me: str) -> list[dict]:
        with self.lock:
            rows = self.db.execute(
                """SELECT u.id, u.display_name FROM friendships f
                   JOIN users u ON u.id = CASE WHEN f.requester_id = ? THEN f.addressee_id ELSE f.requester_id END
                   WHERE f.status = 'accepted' AND (f.requester_id = ? OR f.addressee_id = ?)
                   ORDER BY u.display_name""",
                (me, me, me),
            ).fetchall()
            return [{"id": r["id"], "displayName": r["display_name"]} for r in rows]

    def incoming_requests(self, me: str) -> list[dict]:
        with self.lock:
            rows = self.db.execute(
                """SELECT u.id, u.display_name FROM friendships f JOIN users u ON u.id = f.requester_id
                   WHERE f.addressee_id = ? AND f.status = 'pending' ORDER BY f.created_at""",
                (me,),
            ).fetchall()
            return [{"id": r["id"], "displayName": r["display_name"]} for r in rows]

    def are_friends(self, a: str, b: str) -> bool:
        with self.lock:
            return self.db.execute(
                "SELECT 1 FROM friendships WHERE status = 'accepted' AND "
                "((requester_id = ? AND addressee_id = ?) OR (requester_id = ? AND addressee_id = ?))",
                (a, b, b, a),
            ).fetchone() is not None

    # ---- posts ----------------------------------------------------------------------------------

    def create_post(self, post_id: str, author: str, caption: str, visibility: str, points: int, positions: list[int]) -> dict:
        with self.lock, self.db:
            created = now_ms()
            self.db.execute(
                "INSERT INTO posts (id, author_id, caption, visibility, identification_points, created_at) VALUES (?, ?, ?, ?, ?, ?)",
                (post_id, author, caption, visibility, points, created),
            )
            self.db.executemany(
                "INSERT INTO post_images (post_id, position) VALUES (?, ?)",
                [(post_id, p) for p in positions],
            )
            return {"id": post_id, "createdAt": created}

    def get_post(self, post_id: str) -> dict | None:
        with self.lock:
            row = self.db.execute(
                """SELECT p.*, u.display_name AS author_name FROM posts p JOIN users u ON u.id = p.author_id
                   WHERE p.id = ?""",
                (post_id,),
            ).fetchone()
            if row is None:
                return None
            return self._post(row, self._positions(post_id))

    def delete_post(self, post_id: str, author: str) -> bool:
        with self.lock, self.db:
            cursor = self.db.execute("DELETE FROM posts WHERE id = ? AND author_id = ?", (post_id, author))
            return cursor.rowcount > 0

    def add_report(self, post_id: str, reporter: str, reason: str) -> None:
        with self.lock, self.db:
            self.db.execute(
                "INSERT INTO reports (post_id, reporter_id, reason, created_at) VALUES (?, ?, ?, ?)",
                (post_id, reporter, reason, now_ms()),
            )

    def report_count(self, post_id: str) -> int:
        with self.lock:
            return self.db.execute("SELECT COUNT(*) FROM reports WHERE post_id = ?", (post_id,)).fetchone()[0]

    # ---- feed -----------------------------------------------------------------------------------

    def friend_feed(self, me: str, before: int, limit: int) -> list[dict]:
        """Posts meus e de amigos (de qualquer visibilidade), mais recentes primeiro."""
        with self.lock:
            rows = self.db.execute(
                """SELECT p.*, u.display_name AS author_name FROM posts p JOIN users u ON u.id = p.author_id
                   WHERE p.created_at < ? AND (p.author_id = ? OR p.author_id IN (
                       SELECT CASE WHEN requester_id = ? THEN addressee_id ELSE requester_id END
                       FROM friendships WHERE status = 'accepted' AND (requester_id = ? OR addressee_id = ?)))
                   ORDER BY p.created_at DESC LIMIT ?""",
                (before, me, me, me, me, limit),
            ).fetchall()
            return [self._post(r, self._positions(r["id"])) for r in rows]

    def public_feed(self, viewer: str, before: int, limit: int) -> list[dict]:
        """Posts públicos de quem não é amigo nem o próprio viewer.
        Assim cada post aparece em uma única das duas listas do feed (amigos ou público)."""
        with self.lock:
            rows = self.db.execute(
                """SELECT p.*, u.display_name AS author_name FROM posts p JOIN users u ON u.id = p.author_id
                   WHERE p.visibility = 'public' AND p.created_at < ? AND p.author_id != ?
                   AND p.author_id NOT IN (
                       SELECT CASE WHEN requester_id = ? THEN addressee_id ELSE requester_id END
                       FROM friendships WHERE status = 'accepted' AND (requester_id = ? OR addressee_id = ?))
                   ORDER BY p.created_at DESC LIMIT ?""",
                (before, viewer, viewer, viewer, viewer, limit),
            ).fetchall()
            return [self._post(r, self._positions(r["id"])) for r in rows]

    # ---- internos -------------------------------------------------------------------------------

    def _positions(self, post_id: str) -> list[int]:
        return [r["position"] for r in self.db.execute(
            "SELECT position FROM post_images WHERE post_id = ? ORDER BY position", (post_id,))]

    @staticmethod
    def _post(row, positions: list[int]) -> dict:
        return {
            "id": row["id"],
            "authorId": row["author_id"],
            "authorName": row["author_name"],
            "caption": row["caption"],
            "visibility": row["visibility"],
            "identificationPoints": row["identification_points"],
            "createdAt": row["created_at"],
            "imagePositions": positions,
        }
