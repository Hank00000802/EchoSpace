import { clear, h, setAccent } from "../dom";
import { categoryLabel, issueText, phaseLabel } from "../labels";
import type { MemoryCardView, SessionView } from "../types";
import { createMemoryCardTile, createSourceReference } from "./memoryCard";

export type OpenCard = (card: MemoryCardView) => void;

function empty(message: string): HTMLElement {
  return h("p", { class: "empty", text: message });
}

/** Memory Gallery: every card, in exported order. */
export function renderGallery(root: HTMLElement, data: SessionView, open: OpenCard): void {
  clear(root);
  if (data.memoryCards.length === 0) {
    root.append(empty("這個 Session 還沒有記憶卡。"));
    return;
  }
  root.append(h("div", { class: "grid" }, ...data.memoryCards.map((card) => createMemoryCardTile(card, open))));
}

/** Temporal Classification: groups exactly as listed in the JSON (empty groups shown as empty). */
export function renderClassification(root: HTMLElement, data: SessionView, open: OpenCard): void {
  clear(root);
  if (data.memoryCards.length === 0) {
    root.append(empty("這個 Session 還沒有記憶卡，因此沒有時間分類。"));
    return;
  }
  const byId = new Map(data.memoryCards.map((c) => [c.cardId, c] as const));
  for (const group of data.temporalClassification) {
    const cards = group.cardIds.map((id) => byId.get(id)).filter((c): c is MemoryCardView => c !== undefined);
    // "Transition" is no longer used by the Unity app (connection cards replaced it). Hide it while
    // empty; if old data does contain Transition cards, it is still shown so nothing gets hidden.
    if (group.category === "Transition" && cards.length === 0) continue;
    root.append(
      h(
        "section",
        { class: "group" },
        h("h2", { class: "group__title", text: categoryLabel(group.category) + "（" + cards.length + "）" }),
        cards.length === 0
          ? empty("沒有卡片。")
          : h("div", { class: "grid" }, ...cards.map((card) => createMemoryCardTile(card, open))),
      ),
    );
  }
}

/** Connection Cards: reflection text + exactly ONE source memory card (by sourceCardId). */
export function renderConnections(root: HTMLElement, data: SessionView, open: OpenCard): void {
  clear(root);
  if (data.connectionCards.length === 0) {
    root.append(empty("這個 Session 沒有連結卡片。"));
    return;
  }
  const byId = new Map(data.memoryCards.map((c) => [c.cardId, c] as const));
  const list = h("div", { class: "connections" });
  for (const conn of data.connectionCards) {
    const source = byId.get(conn.sourceCardId);
    const article = h(
      "article",
      { class: "connection" },
      conn.displayTitle ? h("h3", { class: "connection__title", text: conn.displayTitle }) : null,
      h("p", { class: "connection__text" + (conn.transitionText ? "" : " muted"), text: conn.transitionText || "（沒有文字內容）" }),
      h(
        "div",
        { class: "connection__source" },
        h("span", { class: "connection__label", text: "來源：" + phaseLabel(conn.sourcePhase) }),
        source ? createSourceReference(source, open) : h("p", { class: "warn", text: "找不到來源記憶卡（資料不完整）。" }),
      ),
    );
    setAccent(article, conn.groupColor);
    list.append(article);
  }
  root.append(list);
}

export function renderIssues(root: HTMLElement, data: SessionView): void {
  clear(root);
  if (data.issues.length === 0) return;
  root.append(
    h(
      "details",
      { class: "issues" },
      h("summary", { text: "資料檢查提示（" + data.issues.length + "）" }),
      h("ul", {}, ...data.issues.map((i) => h("li", { text: issueText(i.code) + (i.ref ? "　[" + i.ref + "]" : "") }))),
    ),
  );
}
