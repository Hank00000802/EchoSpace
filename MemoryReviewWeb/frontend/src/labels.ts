// Display text. Lookups use Map so odd keys from data can never hit Object.prototype.

const CATEGORY_LABELS = new Map<string, string>([
  ["Unclassified", "未分類"],
  ["Past", "過去"],
  ["Transition", "過渡中"],
  ["Present", "現在"],
]);

export function categoryLabel(category: string): string {
  return CATEGORY_LABELS.get(category) ?? (category || "未分類");
}

export function phaseLabel(phase: string): string {
  if (phase === "Past") return "過去的記憶";
  if (phase === "Present") return "現在的記憶";
  return phase ? phase : "來源記憶";
}

const ISSUE_TEXT = new Map<string, string>([
  ["card_malformed", "有一筆記憶卡格式不正確，已略過"],
  ["card_id_invalid", "有一筆記憶卡的 ID 不正確，已略過"],
  ["card_id_duplicate", "記憶卡 ID 重複，只顯示第一筆"],
  ["screenshot_path_invalid", "截圖路徑不合法，已不顯示截圖"],
  ["screenshot_file_missing", "找不到截圖檔案"],
  ["screenshot_file_invalid", "截圖檔案不是有效的 PNG"],
  ["screenshot_integrity_mismatch", "截圖檔案與匯出紀錄不一致，已不顯示"],
  ["classification_unknown_card", "時間分類中包含不存在的記憶卡"],
  ["card_in_multiple_groups", "記憶卡出現在多個分類中，只顯示第一個"],
  ["card_not_classified", "記憶卡未出現在時間分類中，已依其自身分類顯示"],
  ["category_mismatch", "記憶卡自身的分類與時間分類群組不一致"],
  ["connection_malformed", "有一張連結卡片格式不正確，已略過"],
  ["connection_id_invalid", "有一張連結卡片 ID 不正確或重複，已略過"],
  ["connection_source_missing", "連結卡片找不到來源記憶卡"],
  ["connection_phase_mismatch", "連結卡片的來源階段與記憶卡目前分類不同"],
]);

export function issueText(code: string): string {
  return ISSUE_TEXT.get(code) ?? "資料檢查提示（" + code + "）";
}

const ERROR_TEXT = new Map<string, string>([
  ["invalid_session_id", "Session ID 格式不正確（需為 8–40 個英數字、底線或連字號）。"],
  ["session_not_available", "找不到此 Session，或研究者尚未允許查看。請確認 ID，並確認後端啟動時已用 --session 指定。"],
  ["unsupported_schema", "不支援此 Session 的資料版本（schemaVersion）。"],
  ["session_mismatch", "session.json 與資料夾的 Session ID 不一致，為避免資料混用已拒絕載入。"],
  ["session_unreadable", "session.json 無法讀取或格式損毀。"],
  ["internal_error", "伺服器發生內部錯誤。"],
  ["network", "無法連線到本地伺服器，請確認後端已啟動。"],
]);

export function errorText(code: string): string {
  return ERROR_TEXT.get(code) ?? "載入失敗（" + code + "）。";
}

/** ISO UTC -> local time; falls back to the raw text if it cannot be parsed. */
export function formatTime(iso: string): string {
  if (!iso) return "—";
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString("zh-TW", { hour12: false });
}
