import { trashStamp, trashedConnections, trashedIds, trashedMemory, visibleConnections, visibleMemory } from "../filter";
import { clear, h, setAccent } from "../dom";
import { categoryLabel, formatTime, issueText, phaseLabel } from "../labels";
import type { ConnectionCardView, MemoryCardView, SessionView } from "../types";
import { createMemoryCardTile, createSourceReference } from "./memoryCard";

export type OpenCard = (card: MemoryCardView) => void;

export interface ReviewActions {
  openMemory: OpenCard;
  deleteMemory: (card: MemoryCardView) => void;
  deleteConnection: (conn: ConnectionCardView) => void;
  restoreMemory: (card: MemoryCardView) => void;
  restoreConnection: (conn: ConnectionCardView) => void;
}

function empty(message: string): HTMLElement {
  return h("p", { class: "empty", text: message });
}

function stop(handler: () => void): EventListener {
  return (event) => {
    event.preventDefault();
    event.stopPropagation();
    handler();
  };
}

export function createConnectionArticle(
  conn: ConnectionCardView,
  source: MemoryCardView | undefined,
  sourceTrashed: boolean,
  actions: ReviewActions,
  mode: "live" | "trash",
  note?: string,
): HTMLElement {
  const sourceBlock = source && !sourceTrashed
    ? createSourceReference(source, actions.openMemory)
    : h("p", { class: "warn", text: sourceTrashed ? "來源記憶卡已移到垃圾桶。" : "找不到來源記憶卡（資料不完整）。" });
  const article = h(
    "article",
    { class: "connection" + (mode === "trash" ? " connection--trashed" : "") },
    conn.displayTitle ? h("h3", { class: "connection__title", text: conn.displayTitle }) : null,
    h("p", { class: "connection__text" + (conn.transitionText ? "" : " muted"), text: conn.transitionText || "（沒有文字內容）" }),
    h(
      "div",
      { class: "connection__source" },
      h("span", { class: "connection__label", text: "來源：" + phaseLabel(conn.sourcePhase) }),
      sourceBlock,
    ),
    note ? h("p", { class: "card__meta", text: note }) : null,
    mode === "trash"
      ? h("button", { class: "btn", text: "還原", attrs: { type: "button" }, on: { click: stop(() => actions.restoreConnection(conn)) } })
      : h("button", {
          class: "btn btn--danger-ghost",
          text: "刪除",
          attrs: { type: "button", "aria-label": "刪除這張連結卡片" },
          on: { click: stop(() => actions.deleteConnection(conn)) },
        }),
  );
  setAccent(article, conn.groupColor);
  return article;
}

/** Memory Gallery: every visible card, in exported order. */
export function renderGallery(root: HTMLElement, data: SessionView, actions: ReviewActions): void {
  clear(root);
  const cards = visibleMemory(data);
  if (data.memoryCards.length === 0) {
    root.append(empty("這個 Session 還沒有記憶卡。"));
    return;
  }
  if (cards.length === 0) {
    root.append(empty("可見的記憶卡都在垃圾桶。可到「垃圾桶」還原。"));
    return;
  }
  root.append(
    h("div", { class: "grid" }, ...cards.map((card) => createMemoryCardTile(card, actions.openMemory, actions.deleteMemory))),
  );
}

/** Temporal Classification: groups exactly as listed in the JSON (empty groups shown as empty). */
export function renderClassification(root: HTMLElement, data: SessionView, actions: ReviewActions): void {
  clear(root);
  if (data.memoryCards.length === 0) {
    root.append(empty("這個 Session 還沒有記憶卡，因此沒有時間分類。"));
    return;
  }
  const hidden = trashedIds(data, "memory");
  const byId = new Map(visibleMemory(data).map((c) => [c.cardId, c] as const));
  for (const group of data.temporalClassification) {
    const cards = group.cardIds.map((id) => byId.get(id)).filter((c): c is MemoryCardView => c !== undefined);
    if (group.category === "Transition" && cards.length === 0) continue;
    const originalCount = group.cardIds.filter((id) => !hidden.has(id)).length;
    root.append(
      h(
        "section",
        { class: "group" },
        h("h2", { class: "group__title", text: categoryLabel(group.category) + "（" + originalCount + "）" }),
        cards.length === 0
          ? empty("沒有卡片。")
          : h("div", { class: "grid" }, ...cards.map((card) => createMemoryCardTile(card, actions.openMemory, actions.deleteMemory))),
      ),
    );
  }
}

/** Connection Cards: reflection text + exactly ONE source memory card (by sourceCardId). */
export function renderConnections(root: HTMLElement, data: SessionView, actions: ReviewActions): void {
  clear(root);
  const listItems = visibleConnections(data);
  if (data.connectionCards.length === 0) {
    root.append(empty("這個 Session 沒有連結卡片。"));
    return;
  }
  if (listItems.length === 0) {
    root.append(empty("可見的連結卡片都在垃圾桶。可到「垃圾桶」還原。"));
    return;
  }
  const hiddenMemory = trashedIds(data, "memory");
  const byId = new Map(data.memoryCards.map((c) => [c.cardId, c] as const));
  const list = h("div", { class: "connections" });
  for (const conn of listItems) {
    const source = byId.get(conn.sourceCardId);
    list.append(
      createConnectionArticle(conn, source, !!(source && hiddenMemory.has(source.cardId)), actions, "live"),
    );
  }
  root.append(list);
}

function movedNote(data: SessionView, kind: "memory" | "connection", cardId: string): string {
  const stamp = trashStamp(data, kind, cardId);
  return stamp ? "移入時間 " + formatTime(stamp) : "已從回顧畫面隱藏";
}

export function renderTrash(root: HTMLElement, data: SessionView, actions: ReviewActions): void {
  clear(root);
  const memories = trashedMemory(data);
  const connections = trashedConnections(data);
  const total = memories.length + connections.length;
  const head = h(
    "div",
    { class: "trash-page__head" },
    h("h2", { text: "垃圾桶" }),
    h("p", { class: "trash-page__count", text: total === 0 ? "目前沒有卡片" : "共 " + total + " 張可還原" }),
    h("p", { class: "muted", text: "刪除只會從回顧畫面隱藏，不會改到原始研究資料。按「還原」即可回到圖庫、分類與時間線。" }),
  );
  if (total === 0) {
    root.append(h("section", { class: "trash-page" }, head, h("div", { class: "trash-empty" }, empty("垃圾桶是空的。從卡片上按「刪除」後會出現在這裡。"))));
    return;
  }
  const byId = new Map(data.memoryCards.map((c) => [c.cardId, c] as const));
  root.append(
    h(
      "section",
      { class: "trash-page" },
      head,
      memories.length === 0
        ? null
        : h(
            "div",
            { class: "group" },
            h("h3", { class: "group__title", text: "記憶卡（" + memories.length + "）" }),
            h(
              "div",
              { class: "grid" },
              ...memories.map((card) =>
                createMemoryCardTile(card, actions.openMemory, undefined, actions.restoreMemory, movedNote(data, "memory", card.cardId)),
              ),
            ),
          ),
      connections.length === 0
        ? null
        : h(
            "div",
            { class: "group" },
            h("h3", { class: "group__title", text: "連結卡片（" + connections.length + "）" }),
            h(
              "div",
              { class: "connections" },
              ...connections.map((conn) => {
                const source = byId.get(conn.sourceCardId);
                return createConnectionArticle(
                  conn,
                  source,
                  false,
                  actions,
                  "trash",
                  movedNote(data, "connection", conn.cardId),
                );
              }),
            ),
          ),
    ),
  );
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
