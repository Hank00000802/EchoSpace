"""Synthetic Phase 2B-shaped sessions for tests and for a no-Unity demo.

All content is fake. Shape follows SessionExportDtos.cs / SessionExportManager.cs (schemaVersion 1.1),
including the post-processing Unity applies: empty screenshotPath -> null, markerViewport sentinel -> null.

Demo (writes OUTSIDE the repo, then prints the launch command):
    python -m tests.fixtures --out %TEMP%\\echospace_demo
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
import zlib
from pathlib import Path
from typing import Dict, List, Optional

_SAFE_REL = re.compile(r"screenshots/[A-Za-z0-9_-]{1,64}\.png")


def make_png(width: int, height: int, rgb: tuple) -> bytes:
    def chunk(tag: bytes, data: bytes) -> bytes:
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    row = b"\x00" + bytes(rgb) * width
    raw = row * height
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(raw))
        + chunk(b"IEND", b"")
    )


def card(card_id: str, order: int, mtype: str, label: str, text: str, category: str, shot: Optional[str]) -> Dict:
    return {
        "cardId": card_id,
        "createdOrder": order,
        "memoryType": mtype,
        "memoryTypeLabel": label,
        "promptQuestion": "這是一個提示問題",
        "userText": text,
        "worldPosition": {"x": 1.0, "y": 1.5, "z": -2.0},
        "timeCategory": category,
        "screenshotPath": shot,
    }


def write_session(
    research_root: Path,
    session_id: str,
    cards: List[Dict],
    connections: Optional[List[Dict]] = None,
    classification: Optional[List[Dict]] = None,
    png_colors: Optional[Dict[str, tuple]] = None,
    write_pngs: bool = True,
    override_json: Optional[Dict] = None,
) -> Path:
    """Creates {research_root}/{session_id}/session.json (+ screenshots). Returns the session dir."""
    session_dir = research_root / session_id
    (session_dir / "screenshots").mkdir(parents=True, exist_ok=True)
    (session_dir / "revisions").mkdir(exist_ok=True)

    records = []
    for c in cards:
        rel = c.get("screenshotPath")
        if not rel:
            continue
        png = make_png(64, 36, (png_colors or {}).get(c["cardId"], (30, 60, 120)))
        # SAFETY: only ever write files whose relative path is a plain screenshots/<id>.png.
        # Tests feed hostile paths into the JSON on purpose; they must never be used as write targets.
        if write_pngs and _SAFE_REL.fullmatch(rel):
            (session_dir / rel).write_bytes(png)
        records.append(
            {
                "sessionId": session_id,
                "cardId": c["cardId"],
                "relativePath": rel,
                "width": 1280,
                "height": 720,
                "fieldOfView": 70.0,
                "anchorWorldPosition": {"x": 1, "y": 1.5, "z": -2},
                "cameraPosition": {"x": 0, "y": 1.6, "z": 0},
                "cameraRotation": {"x": 0, "y": 0, "z": 0, "w": 1},
                "markerViewport": None,
                "poseSource": "AnchorPlacement",
                "capturedAtUtc": "2026-10-25T09:00:00Z",
                "sha256": hashlib.sha256(png).hexdigest(),
                "sizeBytes": len(png),
            }
        )

    if classification is None:
        classification = []
        for category in ("Unclassified", "Past", "Transition", "Present"):
            classification.append(
                {"category": category, "cardIds": [c["cardId"] for c in cards if c["timeCategory"] == category]}
            )

    doc = {
        "schemaVersion": "1.1",
        "sessionId": session_id,
        "sessionStartedAtUtc": "2026-10-25T08:50:00Z",
        "exportedAtUtc": "2026-10-25T09:10:00Z",
        "exportRevision": 1,
        "exportTrigger": "manual",
        "app": {"unityVersion": "2022.3.62f3", "sceneName": "SampleScene"},
        "counts": {"memoryCards": len(cards), "connectionCards": len(connections or []), "skippedUnannotatedAnchors": 0},
        "memoryCards": cards,
        "temporalClassification": classification,
        "connectionCards": connections or [],
        "screenshots": records,
        "warnings": ["Memory card x has no screenshot (screenshotPath = null)."],
    }
    if override_json:
        doc.update(override_json)
    (session_dir / "session.json").write_text(json.dumps(doc, ensure_ascii=False, indent=2), encoding="utf-8")
    return session_dir


def connection(card_id: str, source: str, phase: str, text: str, order: int = 0) -> Dict:
    return {
        "cardId": card_id,
        "groupId": "g-" + card_id,
        "createdOrder": order,
        "sourceCardId": source,
        "sourcePhase": phase,
        "transitionText": text,
        "displayTitle": "連結 " + card_id,
        "groupColor": "#3366CCFF",
    }


def demo_cards(prefix: str = "a") -> List[Dict]:
    p = prefix
    return [
        card(p + "0000001", 0, "ConcreteMemory", "具體回憶", "小時候的家有一棵很大的榕樹。", "Past", "screenshots/%s0000001.png" % p),
        card(p + "0000002", 1, "FamiliarFeeling", "熟悉感", "<script>alert('xss')</script> & <b>粗體</b>", "Present", "screenshots/%s0000002.png" % p),
        card(p + "0000003", 2, "Uncertain", "不確定", "不確定這算不算回憶。", "Unclassified", None),
        card(p + "0000004", 3, "Relationship", "一段關係", "和朋友一起散步。", "Past", "screenshots/%s0000004.png" % p),
    ]


def demo_connections(prefix: str = "a") -> List[Dict]:
    p = prefix
    return [
        connection("c-" + p + "01", p + "0000001", "Past", "那棵榕樹的陪伴，如今變成了散步時的安定感。", 0),
        connection("c-" + p + "02", p + "0000002", "Present", "現在的熟悉感，延續了過去的某些片刻。", 1),
    ]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True, help="data root to create (must be outside any git repo)")
    args = parser.parse_args()
    root = Path(args.out).resolve()
    research = root / "research"
    write_session(research, "S-20261025-090000-aaaaaa", demo_cards("a"), demo_connections("a"))
    write_session(
        research,
        "S-20261025-100000-bbbbbb",
        demo_cards("b")[:2],
        demo_connections("b")[:1],
        png_colors={"b0000001": (200, 80, 40), "b0000002": (40, 160, 80)},
    )
    print("Demo data written to:", root)
    print(
        "python run.py --data-root \"%s\" --session S-20261025-090000-aaaaaa --session S-20261025-100000-bbbbbb"
        % root
    )


if __name__ == "__main__":
    main()
