import { h } from "../dom";

// Autobiographical-reasoning prompt shown to the participant. Fixed text (not read from session data).
// The sentences are kept verbatim; invisible zero-width characters from copy/paste were removed.
export const GUIDE_TITLE = "自傳式推理引導語";

export const GUIDE_SENTENCES: readonly string[] = [
  "現在，請你以剛才整理好的卡片為基礎，講述一段關於自己的故事。",
  "請在故事中盡量包含你整理好的卡片，並試著說說看自己是如何從過去一路走到現在的以及這些經歷對你的人生又帶來什麼意義。",
  "請用你願意分享的方式就好。",
];

/** Always-visible block (not collapsible, independent of which session is loaded). */
export function createGuidePrompt(): HTMLElement {
  return h(
    "section",
    { class: "guide", attrs: { "aria-label": GUIDE_TITLE } },
    h("h2", { class: "guide__title", text: GUIDE_TITLE }),
    ...GUIDE_SENTENCES.map((sentence) => h("p", { class: "guide__text", text: sentence })),
  );
}
