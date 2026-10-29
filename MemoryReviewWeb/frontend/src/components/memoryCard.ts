import { h } from "../dom";
import { categoryLabel } from "../labels";
import type { MemoryCardView } from "../types";
import { createScreenshot } from "./screenshot";

/** Reusable card tile (Gallery, Temporal groups, and later the 2D/3D timeline). */
export function createMemoryCardTile(card: MemoryCardView, onOpen: (card: MemoryCardView) => void): HTMLElement {
  const text = card.userText.trim();
  return h(
    "button",
    {
      class: "card",
      attrs: { type: "button", "data-card-id": card.cardId, "aria-label": "閱讀記憶卡：" + (card.memoryTypeLabel || card.memoryType) },
      on: { click: () => onOpen(card) },
    },
    createScreenshot(card),
    h(
      "div",
      { class: "card__body" },
      h(
        "div",
        { class: "badges" },
        h("span", { class: "badge badge--type", text: card.memoryTypeLabel || card.memoryType || "記憶" }),
        h("span", { class: "badge badge--" + (card.timeCategory || "Unclassified").toLowerCase().replace(/[^a-z]/g, ""), text: categoryLabel(card.timeCategory) }),
      ),
      h("p", { class: "card__text" + (text ? "" : " muted"), text: text || "（沒有文字內容）" }),
    ),
  );
}

/** Compact one-line reference to a memory card (used by connection cards). */
export function createSourceReference(card: MemoryCardView, onOpen: (card: MemoryCardView) => void): HTMLElement {
  const text = card.userText.trim();
  return h(
    "button",
    { class: "source", attrs: { type: "button", "data-card-id": card.cardId }, on: { click: () => onOpen(card) } },
    createScreenshot(card),
    h(
      "div",
      { class: "source__body" },
      h("span", { class: "badge badge--type", text: card.memoryTypeLabel || card.memoryType || "記憶" }),
      h("span", { class: "source__text", text: text || "（沒有文字內容）" }),
    ),
  );
}
