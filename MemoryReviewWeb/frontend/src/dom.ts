// Tiny DOM helper. Text is ALWAYS inserted as text nodes (never innerHTML), so user text such as
// "<script>..." is displayed literally and never executed.

export type Child = Node | string | null | undefined | false;

export interface Props {
  class?: string;
  text?: string;
  attrs?: Record<string, string>;
  on?: Record<string, EventListener>;
}

export function h<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  props: Props = {},
  ...children: Child[]
): HTMLElementTagNameMap[K] {
  const el = document.createElement(tag);
  if (props.class) el.className = props.class;
  if (props.text !== undefined) el.textContent = props.text;
  if (props.attrs) {
    for (const [name, value] of Object.entries(props.attrs)) {
      const lower = name.toLowerCase();
      if (lower.startsWith("on") || lower === "style" || lower === "srcdoc") {
        throw new Error("Attribute not allowed: " + name);
      }
      el.setAttribute(name, value);
    }
  }
  if (props.on) {
    for (const [name, listener] of Object.entries(props.on)) el.addEventListener(name, listener);
  }
  for (const child of children) {
    if (child === null || child === undefined || child === false) continue;
    el.append(typeof child === "string" ? document.createTextNode(child) : child);
  }
  return el;
}

export function clear(el: Element): void {
  while (el.firstChild) el.removeChild(el.firstChild);
}

const COLOR_RE = /^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$/;

/** Sets a CSS custom property only for a strict #RRGGBB[AA] value (CSSOM, so CSP-safe). */
export function setAccent(el: HTMLElement, color: string | null): void {
  if (color && COLOR_RE.test(color)) el.style.setProperty("--accent", color);
}
