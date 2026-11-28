"""Ordem do feed: amigos e público intercalados.

Padrão: 10 posts de amigos para cada 1 público (PUBLIC_EVERY = 11). Se um dos lados acabar,
o outro segue sozinho, então o feed nunca fica vazio por causa da proporção.

Cada lista tem o próprio cursor (ver app.py): assim um item não é pulado quando as duas listas
andam em ritmos diferentes.
"""

PUBLIC_EVERY = 11


def mix(friends: list, public: list, limit: int, public_every: int = PUBLIC_EVERY) -> list:
    """Devolve até 'limit' pares (origem, post), origem 'friend' ou 'public'."""
    out = []
    fi = 0
    pi = 0
    while len(out) < limit:
        take_public = len(out) % public_every == public_every - 1
        if take_public and pi < len(public):
            out.append(("public", public[pi]))
            pi += 1
        elif fi < len(friends):
            out.append(("friend", friends[fi]))
            fi += 1
        elif pi < len(public):
            out.append(("public", public[pi]))
            pi += 1
        else:
            break
    return out
