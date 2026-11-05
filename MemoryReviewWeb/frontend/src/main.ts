import "./styles.css";
import { ApiError, fetchSession, moveToTrash, restoreFromTrash } from "./api";
import { closeAllDialogs, openCardDialog } from "./components/cardDialog";
import { confirmAction } from "./components/confirm";
import { createGuidePrompt } from "./components/guidePrompt";
import { renderTimeline } from "./components/timeline";
import { hideUndoToast, showUndoToast } from "./components/undoToast";
import {
  renderClassification,
  renderConnections,
  renderGallery,
  renderIssues,
  renderTrash,
  type ReviewActions,
} from "./components/views";
import { visibleConnections, visibleMemory } from "./filter";
import { clear, h } from "./dom";
import { errorText, formatTime } from "./labels";
import type { MemoryCardView, SessionView, TrashItemView } from "./types";

type TabId = "gallery" | "classification" | "connections" | "trash";

const TABS: ReadonlyArray<{ id: TabId; label: string }> = [
  { id: "gallery", label: "記憶圖庫" },
  { id: "classification", label: "時間分類" },
  { id: "connections", label: "連結卡片" },
  { id: "trash", label: "刪除區" },
];

// The current session lives only in memory (no localStorage / cookies): closing the tab forgets it.
let current: SessionView | null = null;
let activeTab: TabId = "gallery";
let requestSeq = 0;
let controller: AbortController | null = null;
let busy = false;

const app = document.getElementById("app");
if (!app) throw new Error("#app not found");

const input = h("input", {
  attrs: {
    id: "session-id",
    type: "text",
    name: "session",
    placeholder: "S-yyyyMMdd-HHmmss-xxxxxx",
    autocomplete: "off",
    spellcheck: "false",
    maxlength: "40",
    "aria-label": "Session ID",
  },
});
const form = h(
  "form",
  { class: "session-form" },
  h("label", { text: "Session ID", attrs: { for: "session-id" } }),
  input,
  h("button", { class: "btn", text: "載入", attrs: { type: "submit" } }),
);
const statusBox = h("div", { class: "status", attrs: { role: "status", "aria-live": "polite" } });
const summaryBox = h("div", { class: "summary" });
const issuesBox = h("div");
const tabBar = h("div", { class: "tabs", attrs: { role: "tablist", "aria-label": "檢視方式" } });
const panel = h("main", { class: "panel", attrs: { role: "tabpanel", tabindex: "-1" } });
const timelineBox = h("div", { class: "timeline-slot" });

app.append(
  h(
    "header",
    { class: "top" },
    h("h1", { text: "EchoSpace 記憶回顧" }),
    h("p", { class: "muted", text: "本機回顧。" }),
    form,
  ),
  createGuidePrompt(),
  statusBox,
  summaryBox,
  issuesBox,
  tabBar,
  panel,
  timelineBox,
);

const actions: ReviewActions = {
  openMemory: openCard,
  deleteMemory: (card) => requestDelete("memory", card.cardId, "這張記憶卡"),
  deleteConnection: (conn) => requestDelete("connection", conn.cardId, "這張連結卡片"),
  restoreMemory: (card) => void restore("memory", card.cardId),
  restoreConnection: (conn) => void restore("connection", conn.cardId),
};

function openCard(card: MemoryCardView): void {
  if (!current) return;
  const hiddenConn = new Set(current.trash.filter((t) => t.kind === "connection").map((t) => t.cardId));
  const related = current.connectionCards.filter((c) => c.sourceCardId === card.cardId && !hiddenConn.has(c.cardId));
  const inTrash = current.trash.some((t) => t.kind === "memory" && t.cardId === card.cardId);
  openCardDialog(
    card,
    related,
    inTrash ? undefined : (target) => requestDelete("memory", target.cardId, "這張記憶卡"),
    inTrash ? (target) => void restore("memory", target.cardId) : undefined,
  );
}

function setStatus(message: string, kind: "info" | "error" = "info"): void {
  clear(statusBox);
  if (message) statusBox.append(h("p", { class: "status__msg status__msg--" + kind, text: message }));
}

function applySession(data: SessionView): void {
  current = data;
  renderSummary();
  renderTabs();
  renderPanel();
  renderIssues(issuesBox, data);
}

function requestDelete(kind: TrashItemView["kind"], cardId: string, label: string): void {
  confirmAction(
    "移到刪除區？",
    label + "會從回顧畫面隱藏，之後可以從刪除區還原。原始研究資料不會被刪除。",
    "移到刪除區",
    () => {
      void trash(kind, cardId);
    },
  );
}

async function trash(kind: TrashItemView["kind"], cardId: string): Promise<void> {
  if (!current || busy) return;
  busy = true;
  try {
    const data = await moveToTrash(current.session.sessionId, kind, cardId);
    applySession(data);
    showUndoToast("已移到刪除區。", () => {
      void restore(kind, cardId);
    });
  } catch (error) {
    const code = error instanceof ApiError ? error.code : "internal_error";
    setStatus(errorText(code), "error");
  } finally {
    busy = false;
  }
}

async function restore(kind: TrashItemView["kind"], cardId: string): Promise<void> {
  if (!current || busy) return;
  busy = true;
  hideUndoToast();
  try {
    applySession(await restoreFromTrash(current.session.sessionId, kind, cardId));
  } catch (error) {
    const code = error instanceof ApiError ? error.code : "internal_error";
    setStatus(errorText(code), "error");
  } finally {
    busy = false;
  }
}

function renderTabs(): void {
  clear(tabBar);
  for (const tab of TABS) {
    const selected = tab.id === activeTab;
    let count = "";
    if (current) {
      if (tab.id === "connections") count = "（" + visibleConnections(current).length + "）";
      else if (tab.id === "trash") count = "（" + current.counts.trashed + "）";
      else count = "（" + visibleMemory(current).length + "）";
    }
    tabBar.append(
      h("button", {
        class: "tab" + (selected ? " tab--active" : ""),
        text: tab.label + count,
        attrs: { type: "button", role: "tab", "aria-selected": String(selected) },
        on: {
          click: () => {
            activeTab = tab.id;
            renderTabs();
            renderPanel();
          },
        },
      }),
    );
  }
}

function renderPanel(): void {
  if (!current) {
    clear(panel);
    clear(timelineBox);
    panel.append(h("p", { class: "empty", text: "請輸入 Session ID 後按「載入」。" }));
    return;
  }
  if (activeTab === "gallery") renderGallery(panel, current, actions);
  else if (activeTab === "classification") renderClassification(panel, current, actions);
  else if (activeTab === "connections") renderConnections(panel, current, actions);
  else renderTrash(panel, current, actions);
  if (activeTab === "trash") clear(timelineBox);
  else renderTimeline(timelineBox, current, actions);
}

function renderSummary(): void {
  clear(summaryBox);
  if (!current) return;
  const { session } = current;
  summaryBox.append(
    /*h("span", { text: "記憶卡 " + visibleMemory(current).length }),
    h("span", { text: "截圖 " + counts.withScreenshot }),
    h("span", { text: "連結卡片 " + visibleConnections(current).length }),
    h("span", { text: "刪除區 " + counts.trashed }),*/
    h("span", { class: "muted", text: "匯出時間 " + formatTime(session.exportedAtUtc) }),
  );
}

function resetView(): void {
  current = null;
  hideUndoToast();
  closeAllDialogs();
  clear(summaryBox);
  clear(issuesBox);
  renderTabs();
  renderPanel();
}

async function loadSession(rawId: string): Promise<void> {
  const sessionId = rawId.trim();
  controller?.abort();
  const seq = ++requestSeq;

  resetView();
  if (!sessionId) {
    setStatus("請輸入 Session ID。", "error");
    return;
  }

  controller = new AbortController();
  setStatus("載入中…");
  try {
    const data = await fetchSession(sessionId, controller.signal);
    if (seq !== requestSeq) return;
    if (!Array.isArray(data.trash)) data.trash = [];
    setStatus("");
    history.replaceState(null, "", "?session=" + encodeURIComponent(data.session.sessionId));
    applySession(data);
  } catch (error) {
    if (seq !== requestSeq) return;
    if (error instanceof DOMException && error.name === "AbortError") return;
    const code = error instanceof ApiError ? error.code : "internal_error";
    setStatus(errorText(code), "error");
  }
}

form.addEventListener("submit", (event) => {
  event.preventDefault();
  void loadSession(input.value);
});

renderTabs();
renderPanel();
renderTimeline(timelineBox, current, actions);

const initial = new URLSearchParams(location.search).get("session");
if (initial) {
  input.value = initial;
  void loadSession(initial);
} else {
  input.focus();
}
