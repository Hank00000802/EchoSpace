import { safeScreenshotUrl } from "../api";
import { h } from "../dom";
import type { MemoryCardView } from "../types";

function placeholder(message: string): HTMLElement {
  return h("div", { class: "shot shot--placeholder", attrs: { role: "img", "aria-label": message } }, h("span", { text: message }));
}

/**
 * Environment screenshot of a memory card, exactly as exported by Unity (the blue triangle
 * marker is already part of the PNG - nothing is drawn on top here).
 * Missing / unreadable screenshots become a placeholder instead of a broken image icon.
 */
export function createScreenshot(card: MemoryCardView, loading: "lazy" | "eager" = "lazy"): HTMLElement {
  const url = card.screenshot ? safeScreenshotUrl(card.screenshot.url) : null;
  if (!card.screenshot || !url) return placeholder("尚無截圖");

  const wrapper = h("div", { class: "shot" });
  const img = h("img", {
    attrs: {
      src: url,
      alt: "記憶卡截圖：" + (card.memoryTypeLabel || card.memoryType),
      loading,
      decoding: "async",
      referrerpolicy: "no-referrer",
    },
  });
  if (card.screenshot.width && card.screenshot.height) {
    img.width = card.screenshot.width;
    img.height = card.screenshot.height;
  }
  img.addEventListener("error", () => wrapper.replaceWith(placeholder("截圖載入失敗")), { once: true });
  wrapper.append(img);
  return wrapper;
}
