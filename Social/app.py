"""API da rede social do BioDex (FastAPI).

Princípios:
- Sem conta com e-mail: o app cria um token anônimo (POST /v1/devices). O servidor guarda só o hash.
- Sem localização: os corpos aceitam só os campos listados; campos extras geram erro.
- Fotos são reescritas sem metadados (ver media.py).
- Visualizar um post exige ser autor, amigo do autor ou o post ser público.
"""

import os
import secrets
import shutil
from pathlib import Path
from typing import List, Optional

from fastapi import Depends, FastAPI, File, Form, Header, HTTPException, Query, UploadFile
from fastapi.responses import FileResponse
from pydantic import BaseModel, ConfigDict, Field

import media
from feed import mix
from store import Store, now_ms

PAGE_MAX = 50


class NameIn(BaseModel):
    model_config = ConfigDict(extra="forbid")
    displayName: str = Field(min_length=1, max_length=40)


class FriendIn(BaseModel):
    model_config = ConfigDict(extra="forbid")
    code: str = Field(min_length=6, max_length=12)


class ReportIn(BaseModel):
    model_config = ConfigDict(extra="forbid")
    postId: str = Field(min_length=1, max_length=64)
    reason: str = Field(min_length=1, max_length=200)


def create_app(data_dir: str) -> FastAPI:
    root = Path(data_dir)
    media_root = root / "media"
    media_root.mkdir(parents=True, exist_ok=True)
    store = Store(str(root / "social.db"))

    app = FastAPI(title="Biodex social", version="0.1.0")

    def current_user(authorization: Optional[str] = Header(default=None)) -> dict:
        if not authorization or not authorization.startswith("Bearer "):
            raise HTTPException(status_code=401, detail="Token ausente.")
        user = store.user_by_token(authorization[len("Bearer "):].strip())
        if user is None:
            raise HTTPException(status_code=401, detail="Token inválido.")
        return user

    def post_folder(post_id: str) -> Path:
        return media_root / post_id

    def remove_media(post_ids: List[str]) -> None:
        for post_id in post_ids:
            shutil.rmtree(post_folder(post_id), ignore_errors=True)

    def view_post(post: dict) -> dict:
        return {
            **post,
            "images": [f"/v1/posts/{post['id']}/images/{p}" for p in post["imagePositions"]],
        }

    # ---- conta ----------------------------------------------------------------------------------

    @app.get("/health")
    def health():
        return {"ok": True}

    @app.post("/v1/devices", status_code=201)
    def create_device():
        token = secrets.token_urlsafe(32)
        user = store.create_user(token)
        return {"token": token, **user}

    @app.get("/v1/me")
    def me(user: dict = Depends(current_user)):
        return user

    @app.put("/v1/me")
    def rename(body: NameIn, user: dict = Depends(current_user)):
        store.set_display_name(user["id"], body.displayName.strip())
        return {**user, "displayName": body.displayName.strip()}

    @app.delete("/v1/me", status_code=204)
    def delete_account(user: dict = Depends(current_user)):
        # Apaga a conta, os posts e as fotos (direito de exclusão da LGPD).
        remove_media(store.delete_user(user["id"]))

    # ---- amizades -------------------------------------------------------------------------------

    @app.get("/v1/friends")
    def friends(user: dict = Depends(current_user)):
        return {"friends": store.friends(user["id"]), "requests": store.incoming_requests(user["id"])}

    @app.post("/v1/friends", status_code=201)
    def add_friend(body: FriendIn, user: dict = Depends(current_user)):
        other = store.user_by_code(body.code)
        if other is None:
            raise HTTPException(status_code=404, detail="Código não encontrado.")
        if other["id"] == user["id"]:
            raise HTTPException(status_code=400, detail="Não dá para adicionar a si mesmo.")
        return {"status": store.request_friend(user["id"], other["id"])}

    @app.post("/v1/friends/{other_id}/accept")
    def accept_friend(other_id: str, user: dict = Depends(current_user)):
        if not store.accept_friend(user["id"], other_id):
            raise HTTPException(status_code=404, detail="Pedido não encontrado.")
        return {"status": "accepted"}

    @app.delete("/v1/friends/{other_id}", status_code=204)
    def remove_friend(other_id: str, user: dict = Depends(current_user)):
        store.remove_friend(user["id"], other_id)

    # ---- posts ----------------------------------------------------------------------------------

    @app.post("/v1/posts", status_code=201)
    async def create_post(
        images: List[UploadFile] = File(...),
        caption: str = Form("", max_length=500),
        visibility: str = Form("friends", pattern="^(friends|public)$"),
        identificationPoints: int = Form(0, ge=0, le=500),
        user: dict = Depends(current_user),
    ):
        if not 1 <= len(images) <= media.MAX_IMAGES_PER_POST:
            raise HTTPException(status_code=400, detail=f"Envie de 1 a {media.MAX_IMAGES_PER_POST} fotos.")

        post_id = secrets.token_hex(16)
        folder = post_folder(post_id)
        folder.mkdir(parents=True)
        try:
            positions = []
            for index, upload in enumerate(images, start=1):
                raw = await upload.read(media.MAX_IMAGE_BYTES + 1)
                try:
                    clean = media.sanitize_image(raw)
                except ValueError as error:
                    raise HTTPException(status_code=400, detail=str(error))
                (folder / f"{index}.jpg").write_bytes(clean)
                positions.append(index)

            created = store.create_post(
                post_id, user["id"], caption.strip(), visibility, identificationPoints, positions)
        except Exception:
            shutil.rmtree(folder, ignore_errors=True)
            raise

        return {"id": post_id, "createdAt": created["createdAt"], "visibility": visibility}

    @app.get("/v1/feed")
    def feed(
        limit: int = Query(20, ge=1, le=PAGE_MAX),
        beforeFriends: Optional[int] = Query(None, description="nextBeforeFriends da página anterior"),
        beforePublic: Optional[int] = Query(None, description="nextBeforePublic da página anterior"),
        user: dict = Depends(current_user),
    ):
        # Um cursor por lista: a página seguinte começa exatamente depois do último item consumido de cada uma.
        start = now_ms() + 1
        cursor_friends = beforeFriends if beforeFriends is not None else start
        cursor_public = beforePublic if beforePublic is not None else start

        friend_posts = store.friend_feed(user["id"], cursor_friends, limit * 2)
        public_posts = store.public_feed(user["id"], cursor_public, limit)
        page = mix(friend_posts, public_posts, limit)

        friend_times = [p["createdAt"] for source, p in page if source == "friend"]
        public_times = [p["createdAt"] for source, p in page if source == "public"]
        return {
            "posts": [view_post(p) for _, p in page],
            "nextBeforeFriends": min(friend_times) if friend_times else cursor_friends,
            "nextBeforePublic": min(public_times) if public_times else cursor_public,
        }

    @app.get("/v1/posts/{post_id}")
    def get_post(post_id: str, user: dict = Depends(current_user)):
        post = visible_post(post_id, user)
        return view_post(post)

    @app.delete("/v1/posts/{post_id}", status_code=204)
    def delete_post(post_id: str, user: dict = Depends(current_user)):
        if not store.delete_post(post_id, user["id"]):
            raise HTTPException(status_code=404, detail="Post não encontrado.")
        remove_media([post_id])

    @app.get("/v1/posts/{post_id}/images/{position}")
    def post_image(post_id: str, position: int, user: dict = Depends(current_user)):
        visible_post(post_id, user)
        path = post_folder(post_id) / f"{position}.jpg"
        if not path.is_file():
            raise HTTPException(status_code=404, detail="Foto não encontrada.")
        return FileResponse(path, media_type="image/jpeg")

    @app.post("/v1/reports", status_code=201)
    def report(body: ReportIn, user: dict = Depends(current_user)):
        visible_post(body.postId, user)
        store.add_report(body.postId, user["id"], body.reason.strip())
        return {"ok": True}

    def visible_post(post_id: str, user: dict) -> dict:
        post = store.get_post(post_id)
        if post is None:
            raise HTTPException(status_code=404, detail="Post não encontrado.")
        allowed = (
            post["authorId"] == user["id"]
            or post["visibility"] == "public"
            or store.are_friends(user["id"], post["authorId"])
        )
        if not allowed:
            # 404 e não 403: não confirma que o post existe para quem não pode vê-lo.
            raise HTTPException(status_code=404, detail="Post não encontrado.")
        return post

    return app


def _default_app() -> FastAPI:
    return create_app(os.environ.get("SOCIAL_DATA_DIR", "data"))


app = _default_app()
