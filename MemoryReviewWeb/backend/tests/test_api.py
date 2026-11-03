"""Run from MemoryReviewWeb/backend:  python -m unittest discover -s tests -t . -v"""

from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from fastapi.testclient import TestClient

from app.config import ConfigError, Settings, build_settings
from app.main import create_app
from tests import fixtures as fx

SA = "S-20261025-090000-aaaaaa"
SB = "S-20261025-100000-bbbbbb"
SC = "S-20261025-110000-cccccc"  # exists on disk but is never allowed


def snapshot(root: Path) -> dict:
    return {
        str(p.relative_to(root)): (p.stat().st_mtime_ns, hashlib.sha256(p.read_bytes()).hexdigest())
        for p in sorted(root.rglob("*"))
        if p.is_file()
    }


class ApiTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        self.research = self.root / "research"
        fx.write_session(self.research, SA, fx.demo_cards("a"), fx.demo_connections("a"))
        fx.write_session(
            self.research,
            SB,
            fx.demo_cards("b")[:2],
            fx.demo_connections("b")[:1],
            png_colors={"b0000001": (200, 80, 40)},
        )
        fx.write_session(self.research, SC, fx.demo_cards("c"))
        self.client = self.make_client({SA, SB})

    def tearDown(self) -> None:
        self._tmp.cleanup()

    def make_client(self, allowed, dist=None) -> TestClient:
        settings = Settings(data_root=self.root, allowed_sessions=frozenset(allowed), frontend_dist=dist)
        return TestClient(create_app(settings), base_url="http://127.0.0.1:8765", raise_server_exceptions=False)

    def view(self, sid=SA) -> dict:
        r = self.client.get("/api/sessions/" + sid)
        self.assertEqual(r.status_code, 200, r.text)
        return r.json()

    # ---------------------------------------------------------------- content

    def test_memory_gallery_fields_and_order(self):
        v = self.view()
        self.assertEqual(v["session"]["schemaVersion"], "1.1")
        self.assertEqual([c["cardId"] for c in v["memoryCards"]], ["a0000001", "a0000002", "a0000003", "a0000004"])
        first = v["memoryCards"][0]
        self.assertEqual(first["memoryTypeLabel"], "具體回憶")
        self.assertEqual(first["timeCategory"], "Past")
        self.assertEqual(first["screenshot"]["url"], "/api/sessions/%s/cards/a0000001/screenshot" % SA)
        self.assertIsNone(v["memoryCards"][2]["screenshot"])
        self.assertEqual(v["counts"], {"memoryCards": 4, "connectionCards": 2, "withScreenshot": 3, "trashed": 0})
        self.assertEqual(v["trash"], [])

    def test_only_display_fields_are_returned(self):
        text = self.client.get("/api/sessions/" + SA).text
        for leaked in ("worldPosition", "sha256", "cameraPosition", "groupId", "warnings", "anchorWorldPosition", "markerViewport", "screenshotPath", str(self.root)):
            self.assertNotIn(leaked, text)

    def test_classification_groups_follow_json(self):
        v = self.view()
        groups = {g["category"]: g["cardIds"] for g in v["temporalClassification"]}
        self.assertEqual(groups["Past"], ["a0000001", "a0000004"])
        self.assertEqual(groups["Present"], ["a0000002"])
        self.assertEqual(groups["Unclassified"], ["a0000003"])
        self.assertEqual(groups["Transition"], [])
        self.assertEqual([g["category"] for g in v["temporalClassification"]], ["Unclassified", "Past", "Transition", "Present"])

    def test_classification_derived_when_missing(self):
        fx.write_session(self.research, SA, fx.demo_cards("a"), classification=[])
        v = self.view()
        groups = {g["category"]: g["cardIds"] for g in v["temporalClassification"]}
        self.assertEqual(groups, {"Unclassified": ["a0000003"], "Past": ["a0000001", "a0000004"], "Present": ["a0000002"]})

    def test_connection_cards_use_single_source(self):
        v = self.view()
        cc = v["connectionCards"]
        self.assertEqual([(c["cardId"], c["sourceCardId"], c["sourcePhase"]) for c in cc], [("c-a01", "a0000001", "Past"), ("c-a02", "a0000002", "Present")])
        for c in cc:
            self.assertTrue(c["sourceFound"])
            self.assertNotIn("pastCard", c)
            self.assertNotIn("presentCard", c)
        self.assertIn("榕樹", cc[0]["transitionText"])

    def test_connection_with_missing_source_is_flagged_not_hidden(self):
        fx.write_session(self.research, SA, fx.demo_cards("a"), [fx.connection("c-a99", "zzzzzzzz", "Past", "孤兒")])
        v = self.view()
        self.assertFalse(v["connectionCards"][0]["sourceFound"])
        self.assertIn(("connection_source_missing", "c-a99"), [(i["code"], i["ref"]) for i in v["issues"]])

    def test_bad_group_color_is_dropped(self):
        conn = fx.connection("c-a01", "a0000001", "Past", "t")
        conn["groupColor"] = "red;background:url(http://evil)"
        fx.write_session(self.research, SA, fx.demo_cards("a"), [conn])
        self.assertIsNone(self.view()["connectionCards"][0]["groupColor"])

    def test_html_text_is_returned_verbatim_as_data(self):
        v = self.view()
        self.assertEqual(v["memoryCards"][1]["userText"], "<script>alert('xss')</script> & <b>粗體</b>")
        self.assertEqual(self.client.get("/api/sessions/" + SA).headers["content-type"], "application/json")

    def test_empty_session(self):
        fx.write_session(self.research, SA, [], [])
        v = self.view()
        self.assertEqual(v["memoryCards"], [])
        self.assertEqual(v["connectionCards"], [])
        self.assertEqual(v["counts"]["memoryCards"], 0)

    # ---------------------------------------------------------------- screenshots

    def test_screenshot_served_and_valid_png(self):
        r = self.client.get("/api/sessions/%s/cards/a0000001/screenshot" % SA)
        self.assertEqual(r.status_code, 200)
        self.assertEqual(r.headers["content-type"], "image/png")
        self.assertTrue(r.content.startswith(b"\x89PNG"))
        self.assertEqual(r.headers["cache-control"], "no-store")

    def test_card_without_screenshot_is_404(self):
        self.assertEqual(self.client.get("/api/sessions/%s/cards/a0000003/screenshot" % SA).status_code, 404)

    def test_missing_png_gives_placeholder_state_and_issue(self):
        (self.research / SA / "screenshots" / "a0000001.png").unlink()
        v = self.view()
        self.assertIsNone(v["memoryCards"][0]["screenshot"])
        self.assertIn(("screenshot_file_missing", "a0000001"), [(i["code"], i["ref"]) for i in v["issues"]])

    def test_tampered_png_is_not_served(self):
        p = self.research / SA / "screenshots" / "a0000001.png"
        data = bytearray(p.read_bytes())
        data[-20] ^= 0xFF  # same size, different content
        p.write_bytes(bytes(data))
        self.assertEqual(self.client.get("/api/sessions/%s/cards/a0000001/screenshot" % SA).status_code, 404)

    def test_non_png_file_is_rejected(self):
        p = self.research / SA / "screenshots" / "a0000001.png"
        p.write_bytes(b"<html>not a png</html>")
        v = self.view()
        self.assertIsNone(v["memoryCards"][0]["screenshot"])

    def test_screenshot_path_traversal_in_json_is_rejected(self):
        for bad in ("screenshots/../session.json", "../" + SB + "/screenshots/b0000001.png", "/etc/passwd", "C:\\Windows\\win.ini", "screenshots/a0000002.png", "screenshots/a0000001.png\x00.png"):
            cards = fx.demo_cards("a")
            cards[0]["screenshotPath"] = bad
            fx.write_session(self.research, SA, cards)
            v = self.view()
            self.assertIsNone(v["memoryCards"][0]["screenshot"], bad)
            self.assertIn("screenshot_path_invalid", [i["code"] for i in v["issues"]], bad)
            self.assertEqual(self.client.get("/api/sessions/%s/cards/a0000001/screenshot" % SA).status_code, 404, bad)

    # ---------------------------------------------------------------- isolation & safety

    def test_sessions_do_not_mix(self):
        a, b = self.view(SA), self.view(SB)
        self.assertEqual(len(a["memoryCards"]), 4)
        self.assertEqual(len(b["memoryCards"]), 2)
        self.assertTrue(all(c["cardId"].startswith("a") for c in a["memoryCards"]))
        self.assertTrue(all(c["cardId"].startswith("b") for c in b["memoryCards"]))
        self.assertTrue(all(c["sourceCardId"].startswith("b") for c in b["connectionCards"]))
        # a card id from session A cannot be fetched through session B
        self.assertEqual(self.client.get("/api/sessions/%s/cards/a0000001/screenshot" % SB).status_code, 404)

    def test_same_card_id_in_two_sessions_returns_own_png(self):
        fx.write_session(self.research, SA, fx.demo_cards("a"), png_colors={"a0000001": (255, 0, 0)})
        fx.write_session(self.research, SB, fx.demo_cards("a"), png_colors={"a0000001": (0, 0, 255)})
        ra = self.client.get("/api/sessions/%s/cards/a0000001/screenshot" % SA).content
        rb = self.client.get("/api/sessions/%s/cards/a0000001/screenshot" % SB).content
        self.assertNotEqual(ra, rb)
        self.assertEqual(ra, (self.research / SA / "screenshots" / "a0000001.png").read_bytes())
        self.assertEqual(rb, (self.research / SB / "screenshots" / "a0000001.png").read_bytes())

    def test_folder_with_foreign_session_json_is_refused(self):
        (self.research / SB / "session.json").write_bytes((self.research / SA / "session.json").read_bytes())
        r = self.client.get("/api/sessions/" + SB)
        self.assertEqual(r.status_code, 422)
        self.assertEqual(r.json()["error"]["code"], "session_mismatch")

    def test_not_allowed_and_nonexistent_look_identical(self):
        not_allowed = self.client.get("/api/sessions/" + SC)  # on disk, not allowed
        nonexistent = self.client.get("/api/sessions/S-20261025-120000-dddddd")
        self.assertEqual(not_allowed.status_code, 404)
        self.assertEqual(not_allowed.status_code, nonexistent.status_code)
        self.assertEqual(not_allowed.json(), nonexistent.json())
        self.assertEqual(self.client.get("/api/sessions/%s/cards/c0000001/screenshot" % SC).status_code, 404)

    def test_invalid_session_ids_rejected(self):
        for bad in ("short", "a" * 41, "has space!!", "S-2026..-..-xx", "%2e%2e%2f%2e%2e%2fetc"):
            self.assertIn(self.client.get("/api/sessions/" + bad).status_code, (400, 404), bad)
        self.assertEqual(self.client.get("/api/sessions/short").status_code, 400)
        for path in ("/api/sessions/../../etc/passwd", "/api/sessions/..%2f..%2fsession.json", "/api/sessions/"):
            self.assertIn(self.client.get(path).status_code, (400, 404, 405), path)

    def test_no_session_listing_and_no_file_endpoint(self):
        for path in ("/api/sessions", "/api/sessions/", "/research", "/research/" + SA + "/session.json", "/api/files", "/docs", "/openapi.json"):
            self.assertEqual(self.client.get(path).status_code, 404, path)

    def test_host_header_must_be_loopback(self):
        r = self.client.get("/api/health", headers={"Host": "evil.example.com"})
        self.assertEqual(r.status_code, 400)
        self.assertEqual(self.client.get("/api/health", headers={"Host": "localhost:8765"}).status_code, 200)

    def test_no_cors_grant_and_security_headers(self):
        r = self.client.get("/api/sessions/" + SA, headers={"Origin": "http://evil.example.com"})
        self.assertNotIn("access-control-allow-origin", r.headers)
        pre = self.client.options(
            "/api/sessions/" + SA,
            headers={"Origin": "http://evil.example.com", "Access-Control-Request-Method": "GET"},
        )
        self.assertNotIn("access-control-allow-origin", pre.headers)
        self.assertIn("default-src 'none'", r.headers["content-security-policy"])
        self.assertEqual(r.headers["x-content-type-options"], "nosniff")

    def test_read_only(self):
        before = snapshot(self.root)
        self.client.get("/api/sessions/" + SA)
        self.client.get("/api/sessions/%s/cards/a0000001/screenshot" % SA)
        self.client.get("/api/sessions/" + SB)
        self.assertEqual(before, snapshot(self.root))

    # ---------------------------------------------------------------- trash overlay (does not touch research data)

    def test_move_to_trash_and_restore_leaves_session_json_untouched(self):
        session_json = self.research / SA / "session.json"
        before = hashlib.sha256(session_json.read_bytes()).hexdigest()
        moved = self.client.post("/api/sessions/" + SA + "/trash", json={"kind": "memory", "cardId": "a0000001"})
        self.assertEqual(moved.status_code, 200, moved.text)
        body = moved.json()
        self.assertEqual(body["counts"]["trashed"], 1)
        self.assertEqual([(i["kind"], i["cardId"]) for i in body["trash"]], [("memory", "a0000001")])
        self.assertEqual(hashlib.sha256(session_json.read_bytes()).hexdigest(), before)
        self.assertTrue((self.root / "review" / SA / "trash.json").is_file())
        self.assertFalse((self.research / SA / "trash.json").exists())

        restored = self.client.post("/api/sessions/" + SA + "/trash/restore", json={"kind": "memory", "cardId": "a0000001"})
        self.assertEqual(restored.status_code, 200, restored.text)
        self.assertEqual(restored.json()["trash"], [])
        self.assertEqual(self.view()["counts"]["trashed"], 0)
        self.assertEqual(hashlib.sha256(session_json.read_bytes()).hexdigest(), before)

    def test_trash_connection_card_and_idempotent_move(self):
        first = self.client.post("/api/sessions/" + SA + "/trash", json={"kind": "connection", "cardId": "c-a01"})
        second = self.client.post("/api/sessions/" + SA + "/trash", json={"kind": "connection", "cardId": "c-a01"})
        self.assertEqual(first.status_code, 200)
        self.assertEqual(second.status_code, 200)
        self.assertEqual(len(second.json()["trash"]), 1)

    def test_trash_does_not_cross_sessions(self):
        self.client.post("/api/sessions/" + SA + "/trash", json={"kind": "memory", "cardId": "a0000001"})
        other = self.view(SB)
        self.assertEqual(other["trash"], [])
        steal = self.client.post("/api/sessions/" + SB + "/trash", json={"kind": "memory", "cardId": "a0000001"})
        self.assertEqual(steal.status_code, 404)
        missing = self.client.post("/api/sessions/" + SA + "/trash", json={"kind": "connection", "cardId": "a0000001"})
        self.assertEqual(missing.status_code, 404)

    def test_trash_rejected_for_disallowed_session(self):
        r = self.client.post("/api/sessions/" + SC + "/trash", json={"kind": "memory", "cardId": "c0000001"})
        self.assertEqual(r.status_code, 404)
        self.assertEqual(r.json()["error"]["code"], "session_not_available")

    # ---------------------------------------------------------------- schema handling

    def test_unsupported_schema(self):
        fx.write_session(self.research, SA, fx.demo_cards("a"), override_json={"schemaVersion": "2.0"})
        r = self.client.get("/api/sessions/" + SA)
        self.assertEqual((r.status_code, r.json()["error"]["code"]), (422, "unsupported_schema"))

    def test_schema_1_0_without_screenshots_field_still_loads(self):
        cards = fx.demo_cards("a")
        for c in cards:
            c["screenshotPath"] = None
        fx.write_session(self.research, SA, cards, override_json={"schemaVersion": "1.0"})
        doc = json.loads((self.research / SA / "session.json").read_text(encoding="utf-8"))
        doc.pop("screenshots")
        (self.research / SA / "session.json").write_text(json.dumps(doc), encoding="utf-8")
        self.assertEqual(self.view()["counts"]["withScreenshot"], 0)

    def test_corrupt_json(self):
        (self.research / SA / "session.json").write_text("{not json", encoding="utf-8")
        r = self.client.get("/api/sessions/" + SA)
        self.assertEqual((r.status_code, r.json()["error"]["code"]), (422, "session_unreadable"))

    def test_missing_session_json(self):
        (self.research / SA / "session.json").unlink()
        self.assertEqual(self.client.get("/api/sessions/" + SA).status_code, 404)

    def test_duplicate_card_ids_are_flagged(self):
        cards = fx.demo_cards("a")
        cards.append(dict(cards[0]))
        fx.write_session(self.research, SA, cards)
        v = self.view()
        self.assertEqual(len(v["memoryCards"]), 4)
        self.assertIn("card_id_duplicate", [i["code"] for i in v["issues"]])

    # ---------------------------------------------------------------- config

    def test_frontend_placeholder_when_not_built(self):
        r = self.client.get("/")
        self.assertEqual(r.status_code, 200)
        self.assertIn("not built", r.text)

    def test_static_frontend_has_no_traversal(self):
        dist = self.root / "dist"
        dist.mkdir()
        (dist / "index.html").write_text("<!doctype html><title>ok</title>", encoding="utf-8")
        client = self.make_client({SA}, dist=dist)
        self.assertEqual(client.get("/").status_code, 200)
        tail = "research/" + SA + "/session.json"
        for path in ("/..%2f" + tail, "/../" + tail, "/%2e%2e/" + tail, "/" + tail):
            r = client.get(path)
            self.assertNotIn("sessionId", r.text, path)

    def test_config_requires_explicit_session_and_rejects_repo_root(self):
        with self.assertRaises(ConfigError):
            build_settings(str(self.root), [])
        with self.assertRaises(ConfigError):
            build_settings(str(self.root), ["bad id"])
        repo = self.root / "fakerepo"
        (repo / ".git").mkdir(parents=True)
        (repo / "research").mkdir()
        with self.assertRaises(ConfigError):
            build_settings(str(repo), [SA])
        settings = build_settings(str(self.root), [SA, SB + "," + SA])
        self.assertEqual(settings.allowed_sessions, frozenset({SA, SB}))


if __name__ == "__main__":
    unittest.main()
