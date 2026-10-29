import "./styles.css";
import { ApiError, fetchSession } from "./api";
import { closeAllDialogs, openCardDialog } from "./components/cardDialog";
import { renderClassification, renderConnections, renderGallery, renderIssues } from "./components/views";
import { clear, h } from "./dom";
import { errorText, formatTime } from "./labels";
import type { MemoryCardView, SessionView } from "./types";

type TabId = "gallery" | "classification" | "connections";

const TABS: ReadonlyArray<{ id: TabId; label: string }> = [
  { id: "gallery", label: "記憶圖庫" },
  { id: "classification", label: "時間分類" },
  { id: "connections", label: "連結卡片" },
];

// The current session lives only in memory (no localStorage / cookies): closing the tab forgets it.
let current: SessionView | null = null;
let activeTab: TabId = "gallery";
let requestSeq = 0;
let controller: AbortController | null = null;

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

app.append(
  h(
    "header",
    { class: "top" },
    h("h1", { text: "EchoSpace 記憶回顧" }),
    h("p", { class: "muted", text: "本機唯讀檢視。僅能開啟研究者在啟動時允許的 Session。" }),
    form,
  ),
  statusBox,
  summaryBox,
  issuesBox,
  tabBar,
  panel,
);

function openCard(card: MemoryCardView): void {
  const related = current ? current.connectionCards.filter((c) => c.sourceCardId === card.cardId) : [];
  openCardDialog(card, related);
}

function setStatus(message: string, kind: "info" | "error" = "info"): void {
  clear(statusBox);
  if (message) statusBox.append(h("p", { class: "status__msg status__msg--" + kind, text: message }));
}

function renderTabs(): void {
  clear(tabBar);
  for (const tab of TABS) {
    const selected = tab.id === activeTab;
    const count =
      current === null
        ? ""
        : tab.id === "connections"
          ? "（" + current.counts.connectionCards + "）"
          : "（" + current.counts.memoryCards + "）";
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
    panel.append(h("p", { class: "empty", text: "請輸入 Session ID 後按「載入」。" }));
    return;
  }
  if (activeTab === "gallery") renderGallery(panel, current, openCard);
  else if (activeTab === "classification") renderClassification(panel, current, openCard);
  else renderConnections(panel, current, openCard);
}

function renderSummary(): void {
  clear(summaryBox);
  if (!current) return;
  const { session, counts } = current;
  summaryBox.append(
    h("span", { class: "summary__id", text: session.sessionId }),
    h("span", { text: "記憶卡 " + counts.memoryCards }),
    h("span", { text: "截圖 " + counts.withScreenshot }),
    h("span", { text: "連結卡片 " + counts.connectionCards }),
    h("span", { class: "muted", text: "匯出時間 " + formatTime(session.exportedAtUtc) + "（版本 " + session.exportRevision + "）" }),
  );
}

function resetView(): void {
  current = null;
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

  // Drop everything from the previous session first, so data can never be mixed on screen.
  resetView();
  if (!sessionId) {
    setStatus("請輸入 Session ID。", "error");
    return;
  }

  controller = new AbortController();
  setStatus("載入中…");
  try {
    const data = await fetchSession(sessionId, controller.signal);
    if (seq !== requestSeq) return; // a newer request replaced this one
    current = data;
    setStatus("");
    history.replaceState(null, "", "?session=" + encodeURIComponent(data.session.sessionId));
    renderSummary();
    renderTabs();
    renderPanel();
    renderIssues(issuesBox, data);
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

const initial = new URLSearchParams(location.search).get("session");
if (initial) {
  input.value = initial;
  void loadSession(initial);
} else {
  input.focus();
}
