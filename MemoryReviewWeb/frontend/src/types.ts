// Mirrors backend/app/models.py. Only display fields exist here on purpose.

export interface ScreenshotView {
  url: string;
  width: number | null;
  height: number | null;
}

export interface MemoryCardView {
  cardId: string;
  order: number;
  memoryType: string;
  memoryTypeLabel: string;
  promptQuestion: string;
  userText: string;
  timeCategory: string;
  screenshot: ScreenshotView | null;
}

export interface ClassificationGroupView {
  category: string;
  cardIds: string[];
}

export interface ConnectionCardView {
  cardId: string;
  order: number;
  sourceCardId: string;
  sourcePhase: string;
  sourceFound: boolean;
  transitionText: string;
  displayTitle: string;
  groupColor: string | null;
}

export interface IssueView {
  code: string;
  ref: string | null;
}

export interface SessionView {
  session: {
    sessionId: string;
    schemaVersion: string;
    sessionStartedAtUtc: string;
    exportedAtUtc: string;
    exportRevision: number;
  };
  counts: { memoryCards: number; connectionCards: number; withScreenshot: number };
  memoryCards: MemoryCardView[];
  temporalClassification: ClassificationGroupView[];
  connectionCards: ConnectionCardView[];
  issues: IssueView[];
}
