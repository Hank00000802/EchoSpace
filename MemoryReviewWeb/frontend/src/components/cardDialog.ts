import { h } from "../dom";
import { categoryLabel, phaseLabel } from "../labels";
import type { ConnectionCardView, MemoryCardView } from "../types";
import { createScreenshot } from "./screenshot";

/** Opens a modal with the full card. Uses native <dialog> (ESC / focus handling built in). */
export function openCardDialog(
  card: MemoryCardView,
  relatedConnections: ConnectionCardView[],
  onDelete?: (card: MemoryCardView) => void,
  onRestore?: (card: MemoryCardView) => void,
): void {
  const dialog = h("dialog", { class: "dialog", attrs: { "aria-label": "記憶卡內容" } });
  const close = () => dialog.close();

  const related =
    relatedConnections.length === 0
      ? null
      : h(
          "section",
          { class: "dialog__section" },
          h("h3", { text: "由這張記憶延伸的連結卡片" }),
          ...relatedConnections.map((c) =>
            h(
              "blockquote",
              { class: "reflection" },
              h("span", { class: "reflection__meta", text: "來源：" + phaseLabel(c.sourcePhase) }),
              h("p", { text: c.transitionText || "（沒有文字內容）" }),
            ),
          ),
        );

  const parts: Array<Node | null> = [
    h(
      "div",
      { class: "dialog__head" },
      h(
        "div",
        { class: "badges" },
        h("span", { class: "badge badge--type", text: card.memoryTypeLabel || card.memoryType || "記憶" }),
        h("span", { class: "badge", text: categoryLabel(card.timeCategory) }),
      ),
      h("button", { class: "btn btn--ghost", text: "關閉", attrs: { type: "button", "aria-label": "關閉" }, on: { click: close } }),
    ),
    createScreenshot(card, "eager"),
    card.promptQuestion ? h("p", { class: "dialog__prompt", text: card.promptQuestion }) : null,
    h("p", { class: "dialog__text" + (card.userText.trim() ? "" : " muted"), text: card.userText.trim() || "（沒有文字內容）" }),
    related,
    onRestore
      ? h("button", {
          class: "btn",
          text: "從垃圾桶還原",
          attrs: { type: "button" },
          on: {
            click: () => {
              close();
              onRestore(card);
            },
          },
        })
      : onDelete
        ? h("button", {
            class: "btn btn--danger-ghost",
            text: "刪除這張卡片",
            attrs: { type: "button" },
            on: {
              click: () => {
                close();
                onDelete(card);
              },
            },
          })
        : null,
  ];
  // Node.append(null) would insert the text "null", so null entries are dropped explicitly.
  dialog.append(...parts.filter((part): part is Node => part !== null));

  // click on the backdrop (the <dialog> element itself) closes it
  dialog.addEventListener("click", (event) => {
    if (event.target === dialog) close();
  });
  dialog.addEventListener("close", () => dialog.remove(), { once: true });

  document.body.append(dialog);
  dialog.showModal();
}

export function closeAllDialogs(): void {
  document.querySelectorAll("dialog[open]").forEach((d) => (d as HTMLDialogElement).close());
}
