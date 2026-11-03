import { trashedIds, visibleConnections } from "../filter";
import { clear, h } from "../dom";
import { categoryLabel } from "../labels";
import type { MemoryCardView, SessionView } from "../types";
import { createMemoryCardTile } from "./memoryCard";
import { createConnectionArticle, type ReviewActions } from "./views";

type RegionId = "past" | "connections" | "present";

const expanded = new Set<RegionId>();
let boundSessionId: string | null = null;

function empty(message: string): HTMLElement {
  return h("p", { class: "empty", text: message });
}

function cardsIn(data: SessionView, category: string): MemoryCardView[] {
  const hidden = trashedIds(data, "memory");
  const byId = new Map(data.memoryCards.map((c) => [c.cardId, c] as const));
  const group = data.temporalClassification.find((g) => g.category === category);
  const cards = !group
    ? data.memoryCards.filter((c) => c.timeCategory === category)
    : group.cardIds.map((id) => byId.get(id)).filter((c): c is MemoryCardView => c !== undefined);
  return cards.filter((c) => !hidden.has(c.cardId));
}

function regionBody(id: RegionId, data: SessionView, actions: ReviewActions): HTMLElement {
  if (id === "connections") {
    const items = visibleConnections(data);
    if (items.length === 0) return empty("沒有連結卡片。");
    const hiddenMemory = trashedIds(data, "memory");
    const byId = new Map(data.memoryCards.map((c) => [c.cardId, c] as const));
    return h(
      "div",
      { class: "tl-stack" },
      ...items.map((conn) => {
        const source = byId.get(conn.sourceCardId);
        return createConnectionArticle(conn, source, !!(source && hiddenMemory.has(source.cardId)), actions, "live");
      }),
    );
  }
  const cards = cardsIn(data, id === "past" ? "Past" : "Present");
  if (cards.length === 0) return empty(id === "past" ? "沒有過去的卡片。" : "沒有現在的卡片。");
  return h("div", { class: "tl-stack" }, ...cards.map((card) => createMemoryCardTile(card, actions.openMemory, actions.deleteMemory)));
}

function toggleRegion(id: RegionId, root: HTMLElement, data: SessionView, actions: ReviewActions): void {
  if (expanded.has(id)) expanded.delete(id);
  else expanded.add(id);
  renderTimeline(root, data, actions);
}

function nodeButton(
  id: RegionId,
  label: string,
  count: number,
  open: boolean,
  onToggle: () => void,
): HTMLButtonElement {
  const isOpen = open ? "true" : "false";
  return h(
    "button",
    {
      class: "tl-node tl-node--" + id + (open ? " tl-node--open" : ""),
      attrs: {
        type: "button",
        "aria-expanded": isOpen,
        "aria-controls": "tl-panel-" + id,
        "aria-label": (open ? "收回" : "展開") + label + "，共 " + count + " 張",
      },
      on: { click: onToggle },
    },
    h("span", { class: "tl-node__orb", attrs: { "aria-hidden": "true" } }),
    h("span", { class: "tl-node__label", text: label }),
    h("span", { class: "tl-node__count", text: String(count) }),
  );
}

function lane(
  id: RegionId,
  label: string,
  count: number,
  data: SessionView,
  actions: ReviewActions,
  root: HTMLElement,
): HTMLElement {
  const isOpen = expanded.has(id);
  const panel = h(
    "div",
    { class: "tl-panel" + (isOpen ? " tl-panel--open" : ""), attrs: { id: "tl-panel-" + id } },
    isOpen ? regionBody(id, data, actions) : null,
  );
  panel.hidden = !isOpen;
  return h(
    "div",
    { class: "tl-lane" },
    nodeButton(id, label, count, isOpen, () => toggleRegion(id, root, data, actions)),
    panel,
  );
}

/**
 * Independent 2D timeline: three nodes on a life-line.
 * Click a node to expand its details underneath; click again to collapse. Several nodes can stay open.
 * Unclassified stays as a compact row underneath.
 */
export function renderTimeline(root: HTMLElement, data: SessionView | null, actions: ReviewActions): void {
  const sessionId = data ? data.session.sessionId : null;
  if (sessionId !== boundSessionId) {
    expanded.clear();
    boundSessionId = sessionId;
  }

  clear(root);
  if (!data) {
    root.append(
      h(
        "section",
        { class: "tl", attrs: { "aria-label": "時間線" } },
        h("h2", { class: "tl__title", text: "時間線" }),
        h("p", { class: "muted tl__lead", text: "載入 Session 後，這裡會把上方三個分頁的內容彙整成一條從過去到現在的時間線。" }),
      ),
    );
    return;
  }

  const past = cardsIn(data, "Past");
  const present = cardsIn(data, "Present");
  const unclassified = cardsIn(data, "Unclassified");
  const leftoverTransition = cardsIn(data, "Transition");

  const unclassifiedBody =
    unclassified.length === 0 && leftoverTransition.length === 0
      ? empty("沒有未分類的卡片。")
      : h(
          "div",
          { class: "tl-unclassified__rows" },
          unclassified.length > 0
            ? h("div", { class: "tl-stack tl-stack--row" }, ...unclassified.map((card) => createMemoryCardTile(card, actions.openMemory, actions.deleteMemory)))
            : null,
          leftoverTransition.length > 0
            ? h(
                "div",
                { class: "tl-legacy" },
                h("p", { class: "muted", text: categoryLabel("Transition") + "（舊資料，" + leftoverTransition.length + "）" }),
                h("div", { class: "tl-stack tl-stack--row" }, ...leftoverTransition.map((card) => createMemoryCardTile(card, actions.openMemory, actions.deleteMemory))),
              )
            : null,
        );

  root.append(
    h(
      "section",
      { class: "tl", attrs: { "aria-label": "時間線" } },
      h("h2", { class: "tl__title", text: "時間線" }),
      h("p", { class: "muted tl__lead", text: "點選線上的節點展開該階段內容，再點一次收回。可同時展開多個階段。中間是連結卡片（單張來源），不是過去與現在的配對。" }),
      h(
        "div",
        { class: "tl-track" },
        h("div", { class: "tl-track__line", attrs: { "aria-hidden": "true" } }),
        h(
          "div",
          { class: "tl-lanes" },
          lane("past", categoryLabel("Past"), past.length, data, actions, root),
          lane("connections", "連結卡片", visibleConnections(data).length, data, actions, root),
          lane("present", categoryLabel("Present"), present.length, data, actions, root),
        ),
      ),
      h(
        "section",
        { class: "tl-unclassified" },
        h("h3", { class: "tl-unclassified__title", text: categoryLabel("Unclassified") + "（" + unclassified.length + "）" }),
        unclassifiedBody,
      ),
    ),
  );
}
