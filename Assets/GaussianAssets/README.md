# GaussianAssets — 3DGS 場景資料

本資料夾存放 EchoSpace 各房間的 **Gaussian Splat** 匯出資產，供 `SampleScene` 中的 `GaussianSplatRenderer` 引用。

## 儲存方式

| 檔案類型 | 追蹤方式 | 說明 |
|----------|----------|------|
| `*.asset`、`.meta`、資料夾結構 | 一般 Git | Unity 資產定義與 GUID |
| `*.bytes` | **Git LFS** | 大型 splat 二進位 payload（目前約 **53 檔 / ~3.2 GB**） |

Clone 後若場景中 Room 物件顯示 Missing，請執行：

```bash
git lfs install
git lfs pull
```

## 目錄結構

```
GaussianAssets/
├── room01/
│   ├── 5000/          # 測試匯出（medium、Very high）
│   ├── 15000/
│   ├── 8_15000/
│   └── 8_25000/       # room01_8_25000.asset
├── room02/
│   ├── 3_25000/
│   └── test03/
│       └── 3_25000/   # SampleScene：Room02_test03
├── room03/
│   ├── test01/
│   │   ├── 6_25000_very high/
│   │   └── 6_30000_veryhigh/
│   ├── test02/
│   │   └── 4_30000/
│   └── test03/
│       └── 8_30000_exhaustive.asset   # SampleScene：Room03_test03
├── room04/
│   └── room04_01/
│       ├── 8_30000/                   # SampleScene：Room04_01_8_30000
│       └── LumaAI/                    # SampleScene：Room04_01_LumaAI
└── MANIFEST.sha256                    # 各 .bytes 的 SHA256 校驗清單
```

## 單一 Splat 資產的檔案組成

每個 `*.asset` 通常搭配下列 `.bytes`（檔名前綴相同）：

| 後綴 | 內容 |
|------|------|
| `_pos.bytes` | 位置 |
| `_col.bytes` | 顏色 |
| `_oth.bytes` | 尺度、旋轉、不透明度等 |
| `_shs.bytes` | 球諧函數（SH） |
| `_chk.bytes` | （可選）chunk 資料 |

## 完整性驗證（Checksum）

`MANIFEST.sha256` 列出所有 `.bytes` 的 SHA256。在專案根目錄可重新產生並比對：

**PowerShell：**

```powershell
Get-ChildItem -Recurse -File Assets\GaussianAssets -Filter "*.bytes" |
  ForEach-Object {
    $h = Get-FileHash $_.FullName -Algorithm SHA256
    "$($h.Hash)  $($_.FullName.Replace((Get-Location).Path + '\','').Replace('\','/'))"
  } | Sort-Object | Compare-Object (Get-Content Assets\GaussianAssets\MANIFEST.sha256 | Sort-Object)
```

若 `Compare-Object` 無輸出，表示本機檔案與清單一致。

**Git LFS 快速檢查：**

```bash
git lfs ls-files Assets/GaussianAssets
```

## 新增 / 更新 Splat 資產

1. Unity 選單：**Tools → Gaussian Splats → Create GaussianSplatAsset**
2. 輸出至本資料夾下對應子目錄
3. 在 `SampleScene` 的 `GaussianSplatRenderer` 指定新 `.asset`
4. 提交前更新校驗清單並透過 LFS 追蹤新 `.bytes`：

```powershell
# 重新產生 MANIFEST.sha256（於專案根目錄）
Get-ChildItem -Recurse -File Assets\GaussianAssets -Filter "*.bytes" |
  ForEach-Object {
    $h = Get-FileHash $_.FullName -Algorithm SHA256
    "$($h.Hash)  $($_.FullName.Replace((Get-Location).Path + '\','').Replace('\','/'))"
  } | Set-Content Assets\GaussianAssets\MANIFEST.sha256 -Encoding utf8

git add Assets/GaussianAssets/
git lfs status   # 確認 .bytes 走 LFS
```

## 備用下載（離線 / LFS 額度不足時）

若無法使用 Git LFS，可改從團隊共用儲存空間取得完整 `GaussianAssets` 壓縮包，解壓至 `Assets/GaussianAssets/` 後以 `MANIFEST.sha256` 驗證完整性。

> **下載連結（請由團隊維護者填入）：**  
> `（待填入：Google Drive / OneDrive / 校內 NAS 等連結）`

## 相關文件

- [UnityGaussianSplatting 使用說明](https://github.com/aras-p/UnityGaussianSplatting/blob/main/readme.md)
- 專案總覽：[README.md](../../README.md)
