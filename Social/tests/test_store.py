import sqlite3

import pytest

from store import Store, hash_token


@pytest.fixture
def store(tmp_path):
    return Store(str(tmp_path / "social.db"))


def make_user(store, name):
    user = store.create_user(f"token-{name}")
    store.set_display_name(user["id"], name)
    return user["id"]


def test_token_is_stored_only_as_hash(store, tmp_path):
    store.create_user("segredo-123")
    raw = sqlite3.connect(str(tmp_path / "social.db")).execute("SELECT token_hash FROM users").fetchall()
    assert raw == [(hash_token("segredo-123"),)]
    assert "segredo-123" not in str(raw)


def test_codes_are_unique_and_avoid_confusing_letters(store):
    codes = {store.create_user(f"t{i}")["code"] for i in range(200)}
    assert len(codes) == 200
    assert all(not set(c) & set("IO") for c in codes)


def test_friend_request_then_accept(store):
    a, b = make_user(store, "Ana"), make_user(store, "Bia")
    assert store.request_friend(a, b) == "pending"
    assert store.are_friends(a, b) is False
    assert store.accept_friend(b, a) is True
    assert store.are_friends(a, b) is True
    assert store.friends(a) == [{"id": b, "displayName": "Bia"}]


def test_mutual_request_becomes_friendship(store):
    a, b = make_user(store, "Ana"), make_user(store, "Bia")
    store.request_friend(a, b)
    assert store.request_friend(b, a) == "accepted"
    assert store.request_friend(a, b) == "already"


def test_only_addressee_can_accept(store):
    a, b = make_user(store, "Ana"), make_user(store, "Bia")
    store.request_friend(a, b)
    assert store.accept_friend(a, b) is False


def test_remove_friend_both_ways(store):
    a, b = make_user(store, "Ana"), make_user(store, "Bia")
    store.request_friend(a, b)
    store.accept_friend(b, a)
    store.remove_friend(b, a)
    assert store.are_friends(a, b) is False


def test_friend_feed_has_friends_and_self_but_not_strangers(store):
    a, b, c = make_user(store, "Ana"), make_user(store, "Bia"), make_user(store, "Caio")
    store.request_friend(a, b)
    store.accept_friend(b, a)
    store.create_post("p-b", b, "da Bia", "friends", 10, [1])
    store.create_post("p-c", c, "do Caio", "friends", 10, [1])
    store.create_post("p-a", a, "minha", "friends", 10, [1])

    ids = {p["id"] for p in store.friend_feed(a, 10**13, 50)}
    assert ids == {"p-a", "p-b"}


def test_public_feed_only_public_posts(store):
    a, b = make_user(store, "Ana"), make_user(store, "Bia")
    store.create_post("pub", a, "", "public", 0, [1])
    store.create_post("priv", a, "", "friends", 0, [1])
    assert [p["id"] for p in store.public_feed(10**13, 50)] == ["pub"]


def test_delete_post_only_by_author(store):
    a, b = make_user(store, "Ana"), make_user(store, "Bia")
    store.create_post("p1", a, "", "public", 0, [1])
    assert store.delete_post("p1", b) is False
    assert store.delete_post("p1", a) is True
    assert store.get_post("p1") is None


def test_deleting_account_removes_posts_and_returns_ids(store):
    a, b = make_user(store, "Ana"), make_user(store, "Bia")
    store.create_post("p1", a, "", "public", 0, [1, 2])
    store.create_post("p2", b, "", "public", 0, [1])
    removed = store.delete_user(a)
    assert removed == ["p1"]
    assert store.get_post("p1") is None
    assert store.get_post("p2") is not None


def test_post_positions_are_returned_in_order(store):
    a = make_user(store, "Ana")
    store.create_post("p1", a, "legenda", "public", 7, [3, 1, 2])
    post = store.get_post("p1")
    assert post["imagePositions"] == [1, 2, 3]
    assert post["caption"] == "legenda"
    assert post["identificationPoints"] == 7
