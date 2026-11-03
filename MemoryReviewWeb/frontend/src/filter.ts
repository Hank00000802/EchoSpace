import type { ConnectionCardView, MemoryCardView, SessionView, TrashItemView } from "./types";

export function trashedIds(data: SessionView, kind: TrashItemView["kind"]): Set<string> {
  return new Set(data.trash.filter((item) => item.kind === kind).map((item) => item.cardId));
}

export function visibleMemory(data: SessionView): MemoryCardView[] {
  const hidden = trashedIds(data, "memory");
  return data.memoryCards.filter((card) => !hidden.has(card.cardId));
}

export function visibleConnections(data: SessionView): ConnectionCardView[] {
  const hidden = trashedIds(data, "connection");
  return data.connectionCards.filter((card) => !hidden.has(card.cardId));
}

export function trashedMemory(data: SessionView): MemoryCardView[] {
  const hidden = trashedIds(data, "memory");
  return data.memoryCards.filter((card) => hidden.has(card.cardId));
}

export function trashedConnections(data: SessionView): ConnectionCardView[] {
  const hidden = trashedIds(data, "connection");
  return data.connectionCards.filter((card) => hidden.has(card.cardId));
}

export function trashStamp(data: SessionView, kind: TrashItemView["kind"], cardId: string): string {
  const item = data.trash.find((entry) => entry.kind === kind && entry.cardId === cardId);
  return item?.deletedAtUtc ?? "";
}
