from feed import PUBLIC_EVERY, mix


def posts(prefix, n):
    return [{"id": f"{prefix}{i}"} for i in range(n)]


def test_ten_friend_posts_for_each_public_post():
    out = mix(posts("f", 30), posts("p", 5), 22)
    sources = [s for s, _ in out]
    assert sources[PUBLIC_EVERY - 1] == "public"
    assert sources[2 * PUBLIC_EVERY - 1] == "public"
    assert sources.count("public") == 2


def test_only_friends_when_there_is_no_public_post():
    out = mix(posts("f", 5), [], 5)
    assert [s for s, _ in out] == ["friend"] * 5


def test_only_public_when_there_are_no_friends():
    out = mix([], posts("p", 3), 5)
    assert [s for s, _ in out] == ["public"] * 3


def test_respects_limit():
    assert len(mix(posts("f", 30), posts("p", 30), 7)) == 7


def test_keeps_order_inside_each_list():
    out = mix(posts("f", 4), posts("p", 2), 6)
    friend_ids = [p["id"] for s, p in out if s == "friend"]
    public_ids = [p["id"] for s, p in out if s == "public"]
    assert friend_ids == ["f0", "f1", "f2", "f3"]
    assert public_ids == ["p0", "p1"]
