import { clear, h } from "../dom";

let host: HTMLElement | null = null;
let undoHandler: (() => void) | null = null;
let hideTimer = 0;

function ensureHost(): HTMLElement {
  if (host && host.isConnected) return host;
  host = h("div", { class: "undo-toast", attrs: { role: "status", "aria-live": "polite" } });
  host.hidden = true;
  document.body.append(host);
  return host;
}

export function showUndoToast(message: string, onUndo: () => void): void {
  window.clearTimeout(hideTimer);
  undoHandler = onUndo;
  const box = ensureHost();
  clear(box);
  box.append(
    h("span", { text: message }),
    h("button", {
      class: "btn",
      text: "還原",
      attrs: { type: "button" },
      on: {
        click: () => {
          const fn = undoHandler;
          hideUndoToast();
          if (fn) fn();
        },
      },
    }),
  );
  box.hidden = false;
  hideTimer = window.setTimeout(hideUndoToast, 8000);
}

export function hideUndoToast(): void {
  window.clearTimeout(hideTimer);
  undoHandler = null;
  if (host) {
    host.hidden = true;
    clear(host);
  }
}
