# EchoSpace Memory Review Web（Phase 3A）

本機、唯讀的記憶回顧網頁。讀取 Unity 匯出的 `session.json`（schemaVersion 1.x）與 PNG 截圖。
只綁定 `127.0.0.1`，不連網、不寫入、沒有資料庫，不修改任何 Unity 檔案。

```
MemoryReviewWeb/
├─ backend/            FastAPI（唯讀）
│  ├─ run.py           啟動器（固定 127.0.0.1）
│  ├─ app/             config / models / store（資料驗證與讀取）/ main（路由與安全標頭）
│  └─ tests/           unittest（32 項）+ fixtures.py（假資料產生器）
└─ frontend/           Vite vanilla-ts（無執行期依賴）
   └─ src/             api / types / dom / labels / components/*
```

## 啟動

資料不放在 repo 內：`dataRoot` 必須是含有 `research/` 的資料夾，且不能位於任何 Git repo 內。

```powershell
# 一次性：建立 Python 環境
cd MemoryReviewWeb\backend
python -m venv .venv
.\.venv\Scripts\activate
pip install -r requirements.txt

# 啟動（研究者明確指定本次允許查看的 Session）
python run.py --data-root "D:\EchoSpaceData" --session S-20261025-090000-aaaaaa
```

- 網址：`http://127.0.0.1:8765/?session=<SessionId>`（啟動時會在 console 印出）
- 埠號預設 8765（8000 已被 `tools/stt_proxy` 使用），可用 `--port` 修改
- 多個 Session：重複 `--session`，或設環境變數 `ECHOSPACE_REVIEW_SESSIONS`（逗號或空白分隔）

### dataRoot 的決定順序

1. `--data-root`
2. 環境變數 `ECHOSPACE_DATA_DIR`（與 Unity `SessionExportManager` 相同）
3. Windows 預設：`%USERPROFILE%\AppData\LocalLow\DefaultCompany\EchoSpace_2022_3D_Builtin\EchoSpaceData`
   （即 Unity 的 `Application.persistentDataPath/EchoSpaceData`）

Session 資料夾路徑為 `{dataRoot}\research\{SessionId}\session.json`。

### 前端

`frontend/dist/` 是建置結果（已被 gitignore）。若不存在或要修改前端，需要 Node 18+：

```powershell
cd MemoryReviewWeb\frontend
npm install
npm run build        # tsc 型別檢查 + vite build -> dist/，由後端直接提供
npm run dev          # 開發模式：http://127.0.0.1:5173，/api 代理到 8765
```

## 安全設計

| 項目 | 做法 |
|---|---|
| 存取範圍 | 只有啟動參數指定的 Session 可讀；沒有 Session 列表端點；未允許與不存在回傳同樣的 404 |
| Session ID | 不當作身份驗證，只是索引；格式驗證 `[A-Za-z0-9_-]{8,40}`，資料夾必須是 `research/` 的直接子層（擋 `..`、symlink） |
| 圖片 | 前端不能傳路徑。後端依 `session.json` 的 `screenshotPath` 取檔，且必須完全符合 `screenshots/<cardId>.png`、檔名與 cardId 一致、真實路徑在該 Session 的 `screenshots/` 內、PNG 檔頭、`sizeBytes` 與 `sha256`（若 JSON 有記錄）相符 |
| 資料混用 | `session.json` 內的 `sessionId` 必須等於資料夾名稱，否則拒絕（422） |
| 回傳欄位 | 以 pydantic 模型白名單：不回傳座標、相機姿態、sha256、groupId、Unity warnings、任何檔案路徑 |
| 唯讀 | 後端不開啟任何寫入；測試會比對請求前後所有檔案的雜湊與 mtime |
| 文字 | 前端一律用 `textContent`（`dom.ts` 的 `h()`），`userText` 不會被當 HTML；`groupColor` 嚴格驗證 `#RRGGBB[AA]` |
| 瀏覽器端 | 不用 CORS（同源）、`TrustedHost`（只接受 127.0.0.1 / localhost，防 DNS rebinding）、CSP `default-src 'none'`、`no-store`、`nosniff`；不用 localStorage / cookie；關閉 `/docs`、`/openapi.json` |
| Repo | 資料夾拒絕位於 Git repo 內的 dataRoot；根目錄 `.gitignore` 另外擋 `research/`、`session*.json`、`screenshots/`、`revisions/`、`.env`、`node_modules`、`dist` 等 |

## 資料處理規則（schema 1.1）

- 記憶卡依 `createdOrder`（缺少時依陣列順序）排序；`screenshotPath` 為 `null` 顯示「尚無截圖」。
- 時間分類以 JSON 的 `temporalClassification` 為準。空的 `Transition`（過渡中，Unity 已不再使用）不顯示；
  若舊資料中 Transition 有卡片則照常顯示。若整段分類缺失，改由各卡的 `timeCategory` 推導。
- 連結卡片只用 `sourceCardId` 對應「單張」來源卡；`sourcePhase` 只顯示來源階段（過去／現在），不組成 Past–Present 配對。
  找不到來源卡時仍顯示文字，並標示「資料不完整」。
- 截圖已含藍色三角 marker，前端不重畫，也不使用 `markerViewport`。
- 資料問題（路徑不合法、檔案缺失、分類不一致…）會收在頁面上方的「資料檢查提示」，不會讓整頁失敗。

## 測試

```powershell
cd MemoryReviewWeb\backend
python -m unittest discover -s tests -t . -v
```

無 Unity 時可產生假資料（寫到 repo 之外）：

```powershell
python -m tests.fixtures --out "$env:TEMP\echospace_demo"
# 依輸出的指令啟動；內含兩個 Session，同樣的卡片 ID 前綴不同、截圖顏色不同
```

## 下一階段可重用

| 檔案 | 用途 |
|---|---|
| `src/types.ts`、`src/api.ts` | 資料型別與取得 Session（含 AbortSignal 與錯誤碼） |
| `src/dom.ts` | 安全 DOM 建構器 |
| `src/labels.ts` | 分類 / 階段 / 錯誤文字 |
| `components/memoryCard.ts` | `createMemoryCardTile`、`createSourceReference`：可直接放進 2D / 3D 時間軸節點 |
| `components/screenshot.ts` | 截圖 + 缺圖 placeholder |
| `components/cardDialog.ts` | 閱讀單張卡片，並帶出其延伸的連結卡片 |
| `components/views.ts` | 分組與連結卡片的渲染邏輯（`renderClassification` 可作為時間軸分欄的基礎） |
| `backend/app/store.py` | `SessionStore.load_view`：新增時間軸所需欄位（如 `createdOrder`）時只需擴充 `models.py` |
