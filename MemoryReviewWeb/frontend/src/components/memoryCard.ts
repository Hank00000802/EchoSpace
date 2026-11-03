import { h } from "../dom";
import { categoryLabel } from "../labels";
import type { MemoryCardView } from "../types";
import { createScreenshot } from "./screenshot";

function stop(handler: () => void): EventListener {
  return (event) => {
    event.preventDefault();
    event.stopPropagation();
    handler();
  };
}

/** Reusable card tile (Gallery, Temporal groups, timeline, trash). */
export function createMemoryCardTile(
  card: MemoryCardView,
  onOpen: (card: MemoryCardView) => void,
  onDelete?: (card: MemoryCardView) => void,
  onRestore?: (card: MemoryCardView) => void,
  note?: string,
): HTMLElement {
  const text = card.userText.trim();
  const article = h("article", {
    class: "card" + (onRestore ? " card--trashed" : ""),
    attrs: { "data-card-id": card.cardId },
  });
  article.append(
    h(
      "button",
      {
        class: "card__main",
        attrs: { type: "button", "aria-label": "閱讀記憶卡：" + (card.memoryTypeLabel || card.memoryType) },
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
          h("span", {
            class: "badge badge--" + (card.timeCategory || "Unclassified").toLowerCase().replace(/[^a-z]/g, ""),
            text: categoryLabel(card.timeCategory),
          }),
        ),
        h("p", { class: "card__text" + (text ? "" : " muted"), text: text || "（沒有文字內容）" }),
      ),
    ),
  );
  if (note) article.append(h("p", { class: "card__meta", text: note }));
  if (onRestore) {
    article.append(
      h("button", { class: "btn card__action", text: "還原", attrs: { type: "button" }, on: { click: stop(() => onRestore(card)) } }),
    );
  } else if (onDelete) {
    article.append(
      h("button", {
        class: "btn btn--danger-ghost card__action",
        text: "刪除",
        attrs: { type: "button", "aria-label": "刪除這張記憶卡" },
        on: { click: stop(() => onDelete(card)) },
      }),
    );
  }
  return article;
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
