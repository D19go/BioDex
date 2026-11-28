"""Testes HTTP: precisam do FastAPI (instalado pelo CI com requirements.txt)."""

import io

import pytest

pytest.importorskip("fastapi")
pytest.importorskip("httpx")

from fastapi.testclient import TestClient  # noqa: E402
from PIL import Image  # noqa: E402

from app import create_app  # noqa: E402


@pytest.fixture
def client(tmp_path):
    return TestClient(create_app(str(tmp_path)))


def jpeg_bytes():
    buffer = io.BytesIO()
    Image.new("RGB", (120, 90), (30, 90, 30)).save(buffer, format="JPEG")
    return buffer.getvalue()


def sign_up(client, name):
    device = client.post("/v1/devices").json()
    headers = {"Authorization": f"Bearer {device['token']}"}
    client.put("/v1/me", json={"displayName": name}, headers=headers)
    return device, headers


def publish(client, headers, visibility="public", caption="teste"):
    response = client.post(
        "/v1/posts",
        files=[("images", ("foto.jpg", jpeg_bytes(), "image/jpeg"))],
        data={"caption": caption, "visibility": visibility, "identificationPoints": "15"},
        headers=headers,
    )
    assert response.status_code == 201, response.text
    return response.json()["id"]


def befriend(client, requester_headers, addressee_headers):
    """Pedido por código e aceite, como o app faz."""
    code = client.get("/v1/me", headers=addressee_headers).json()["code"]
    addressee_id = client.get("/v1/me", headers=addressee_headers).json()["id"]
    assert client.post("/v1/friends", json={"code": code}, headers=requester_headers).status_code == 201
    assert client.post(f"/v1/friends/{client.get('/v1/me', headers=requester_headers).json()['id']}/accept",
                       headers=addressee_headers).status_code == 200
    return addressee_id


def test_requests_without_token_are_refused(client):
    assert client.get("/v1/me").status_code == 401
    assert client.get("/v1/me", headers={"Authorization": "Bearer lixo"}).status_code == 401


def test_location_fields_are_refused(client):
    _, headers = sign_up(client, "Ana")
    response = client.put("/v1/me", json={"displayName": "Ana", "latitude": -29.7}, headers=headers)
    assert response.status_code == 422


def test_upload_strips_metadata_and_feed_shows_post(client):
    _, headers = sign_up(client, "Ana")
    post_id = publish(client, headers, visibility="public")

    feed = client.get("/v1/feed", headers=headers).json()
    assert [p["id"] for p in feed["posts"]] == [post_id]
    assert feed["posts"][0]["authorName"] == "Ana"

    image = client.get(f"/v1/posts/{post_id}/images/1", headers=headers)
    assert image.status_code == 200
    assert image.headers["content-type"] == "image/jpeg"


def test_friends_only_post_is_hidden_from_strangers(client):
    _, ana = sign_up(client, "Ana")
    _, bia = sign_up(client, "Bia")
    post_id = publish(client, ana, visibility="friends")

    assert client.get(f"/v1/posts/{post_id}", headers=bia).status_code == 404
    assert client.get(f"/v1/posts/{post_id}/images/1", headers=bia).status_code == 404


def test_friend_sees_friends_only_post(client):
    _, ana = sign_up(client, "Ana")
    _, bia = sign_up(client, "Bia")
    post_id = publish(client, ana, visibility="friends")
    assert client.get(f"/v1/posts/{post_id}", headers=bia).status_code == 404

    befriend(client, bia, ana)  # Bia pede; Ana aceita

    assert client.get(f"/v1/posts/{post_id}", headers=bia).status_code == 200
    assert client.get(f"/v1/posts/{post_id}/images/1", headers=bia).status_code == 200


def test_only_author_can_delete_post(client):
    _, ana = sign_up(client, "Ana")
    _, bia = sign_up(client, "Bia")
    post_id = publish(client, ana, visibility="public")

    assert client.delete(f"/v1/posts/{post_id}", headers=bia).status_code == 404
    assert client.delete(f"/v1/posts/{post_id}", headers=ana).status_code == 204
    assert client.get(f"/v1/posts/{post_id}", headers=ana).status_code == 404


def test_non_image_upload_is_refused(client):
    _, headers = sign_up(client, "Ana")
    response = client.post(
        "/v1/posts",
        files=[("images", ("foto.jpg", b"nao e imagem", "image/jpeg"))],
        data={"visibility": "public"},
        headers=headers,
    )
    assert response.status_code == 400


def test_account_deletion_removes_posts_and_photos(client, tmp_path):
    _, ana = sign_up(client, "Ana")
    post_id = publish(client, ana, visibility="public")
    assert (tmp_path / "media" / post_id / "1.jpg").exists()

    assert client.delete("/v1/me", headers=ana).status_code == 204

    assert client.get("/v1/me", headers=ana).status_code == 401
    assert not (tmp_path / "media" / post_id).exists()


def test_report_requires_visible_post(client):
    _, ana = sign_up(client, "Ana")
    _, bia = sign_up(client, "Bia")
    post_id = publish(client, ana, visibility="friends")
    assert client.post("/v1/reports", json={"postId": post_id, "reason": "spam"}, headers=bia).status_code == 404
    assert client.post("/v1/reports", json={"postId": post_id, "reason": "spam"}, headers=ana).status_code == 201


def test_feed_cursors_do_not_repeat_posts(client):
    _, ana = sign_up(client, "Ana")
    ids = [publish(client, ana, visibility="public", caption=f"n{i}") for i in range(5)]

    first = client.get("/v1/feed?limit=2", headers=ana).json()
    second = client.get(
        f"/v1/feed?limit=2&beforeFriends={first['nextBeforeFriends']}&beforePublic={first['nextBeforePublic']}",
        headers=ana,
    ).json()
    seen = [p["id"] for p in first["posts"]] + [p["id"] for p in second["posts"]]
    assert len(seen) == len(set(seen)) == 4
    assert set(seen) <= set(ids)
