"""Response models. Only fields needed for display are listed here: anything not declared
(worldPosition, camera poses, sha256, groupId, Unity warnings, file paths ...) never leaves the server.
"""

from __future__ import annotations

from typing import List, Optional

from pydantic import BaseModel


class ScreenshotView(BaseModel):
    url: str  # server-relative API URL; never a file path
    width: Optional[int] = None
    height: Optional[int] = None


class MemoryCardView(BaseModel):
    cardId: str
    order: int  # 0-based position in the exported order
    memoryType: str
    memoryTypeLabel: str
    promptQuestion: str
    userText: str
    timeCategory: str
    screenshot: Optional[ScreenshotView] = None  # null -> frontend shows a placeholder


class ClassificationGroupView(BaseModel):
    category: str  # raw TimeCategory name from the JSON (Unclassified / Past / Transition / Present ...)
    cardIds: List[str]


class ConnectionCardView(BaseModel):
    cardId: str
    order: int
    sourceCardId: str  # exactly ONE source memory card
    sourcePhase: str  # "Past" / "Present" - which side the source card was on
    sourceFound: bool  # false when sourceCardId is not among memoryCards
    transitionText: str
    displayTitle: str
    groupColor: Optional[str] = None  # validated #RRGGBB[AA] or null


class CountsView(BaseModel):
    memoryCards: int
    connectionCards: int
    withScreenshot: int


class IssueView(BaseModel):
    code: str
    ref: Optional[str] = None  # a card id, never user text


class SessionMetaView(BaseModel):
    sessionId: str
    schemaVersion: str
    sessionStartedAtUtc: str
    exportedAtUtc: str
    exportRevision: int


class SessionView(BaseModel):
    session: SessionMetaView
    counts: CountsView
    memoryCards: List[MemoryCardView]
    temporalClassification: List[ClassificationGroupView]
    connectionCards: List[ConnectionCardView]
    issues: List[IssueView]
