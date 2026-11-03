import { h } from "../dom";

export function confirmAction(title: string, body: string, confirmLabel: string, onConfirm: () => void): void {
  const dialog = h("dialog", { class: "dialog dialog--confirm", attrs: { "aria-label": title } });
  const close = () => dialog.close();
  dialog.append(
    h("h2", { class: "tl-detail__title", text: title }),
    h("p", { class: "dialog__text", text: body }),
    h(
      "div",
      { class: "dialog__actions" },
      h("button", { class: "btn btn--ghost", text: "取消", attrs: { type: "button" }, on: { click: close } }),
      h("button", {
        class: "btn btn--danger",
        text: confirmLabel,
        attrs: { type: "button" },
        on: {
          click: () => {
            close();
            onConfirm();
          },
        },
      }),
    ),
  );
  dialog.addEventListener("click", (event) => {
    if (event.target === dialog) close();
  });
  dialog.addEventListener("close", () => dialog.remove(), { once: true });
  document.body.append(dialog);
  dialog.showModal();
}
