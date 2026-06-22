# EchoSpace

A Unity VR research prototype for **EchoSpace** — a personalized reflective space that combines **3D Gaussian Splatting (3DGS)** reconstructed environments with **identity-integration** activities, including memory marking, life-timeline organization, guided reflection, and perspective switching.

EchoSpace is developed as a thesis / lab prototype for exploring how immersive personal spaces can support self-continuity and reflection during life transitions.

## Contents

* [Overview](#overview)
* [Current Features](#current-features)
* [VR Interaction Flow](#vr-interaction-flow)
* [What's in this repo](#whats-in-this-repo)
* [3DGS Scene Data (Git LFS)](#3dgs-scene-data-git-lfs)
* [Requirements](#requirements)
* [XR Setup Notes](#xr-setup-notes)
* [Main Scene](#main-scene)
* [Status](#status)
* [快速開始（跨機開發）](#快速開始跨機開發)
* [專案結構與外部依賴](#專案結構與外部依賴)
* [開發備忘](#開發備忘)
* [常見問題](#常見問題)
* [License](#license)

## Overview

EchoSpace lets users revisit a personalized 3DGS environment and mark meaningful memory anchors in space. These anchors are later organized into a **Line of Life** structure: **Past**, **Transition**, and **Present**. The system then generates a spatial 3D timeline with reflection prompts that guide users to connect memories across life phases.

The prototype focuses on:

* personalized 3D reconstructed spaces
* memory annotation in VR
* timeline-based identity reflection
* cross-phase reflection prompts
* perspective switching to familiar viewpoints

## Current Features

* **3DGS Scene Viewing**
  Load and view reconstructed room-scale 3D Gaussian Splatting environments in Unity.

* **Memory Anchor Placement**
  Place memory anchors freely in space using XR controller-based preview marker placement.

* **Annotation Panel**
  Select memory types and enter memory descriptions for each anchor.

* **Line of Life Panel**
  Organize memory cards into three phases: **Past**, **Transition**, and **Present**.

* **3D Timeline View**
  Display organized memory cards along a spatial timeline inside the 3DGS environment.

* **Progressive Reflection Flow**
  Users first reflect on each phase, then unlock cross-phase reflection prompts, and finally access an overall reflection prompt.

* **Switch View Mechanism**
  Move the user to a predefined viewpoint linked to a familiar or past-self perspective.

* **XR Controller Support**
  Current development targets **HTC Vive Focus Vision via SteamVR / OpenXR**, with controller ray interaction, UI clicking, marker placement, and mode switching.

## VR Interaction Flow

Current prototype flow:

1. The user explores the 3DGS environment in VR.
2. The user switches to **Marking Mode** and places a memory anchor in space.
3. The **Annotation Panel** opens, allowing the user to choose a memory type and enter memory content.
4. The user opens the **Line of Life Panel** and drags memory cards into **Past**, **Transition**, or **Present**.
5. After completing organization, the system shows a **3D Timeline** across the scene.
6. The user clicks phase reflection buttons to view guided prompts.
7. After all phase prompts are viewed, cross-phase reflection buttons are unlocked.
8. After cross-phase prompts are viewed, the overall reflection prompt is unlocked.
9. The user can start the **Switch View** mechanism and move to a predefined viewpoint.

## What's in this repo

* Unity VR project using **OpenXR** and **XR Interaction Toolkit**
* Annotation / memory anchor system
* Line of Life 2D organization panel
* 3D timeline visualization
* Progressive reflection prompts
* Switch View perspective mechanism
* XR controller interaction logic
* English UI strings
* Vive Focus Vision / SteamVR-oriented XR setup in progress
* 3DGS splat assets under `Assets/GaussianAssets/` (via **Git LFS**)
* [UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting) as a **Git Submodule**

## 3DGS Scene Data (Git LFS)

Large splat binary files (`*.bytes`) under `Assets/GaussianAssets/` are tracked via **Git LFS** (~3.5 GB). Each splat asset includes:

* `*.asset` — Unity GaussianSplat asset definition (regular Git)
* `*_pos.bytes`, `*_col.bytes`, `*_oth.bytes`, `*_shs.bytes` — geometry and appearance data (Git LFS)

After cloning:

```bash
git lfs install
git clone --recurse-submodules https://github.com/Hank00000802/EchoSpace.git
cd EchoSpace
git lfs pull
```

See [Assets/GaussianAssets/README.md](Assets/GaussianAssets/README.md) for directory structure and checksum verification.

## Requirements

* **Unity Editor 2022.3.62f3** (3D template, **Built-in Render Pipeline**)
* **Graphics API:** Direct3D 12 or Vulkan (**not DX11**; required for 3DGS)
* **Color Space:** Linear
* **Git LFS** (required for splat `.bytes` files)
* Unity packages:

  * OpenXR 1.14.x
  * XR Interaction Toolkit 2.6.x
  * VIVE OpenXR Plugin 2.2.0
  * Input System
  * TextMeshPro
* VR headset and controllers

  * Current target: **HTC Vive Focus Vision via SteamVR**
* [UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting) (included as submodule at `External/UnityGaussianSplatting`)

> Product name: `EchoSpace_2022_3D_Builtin`. Do not open with URP/HDRP templates.

## XR Setup Notes

The current Vive setup uses:

* **HTC Vive Controller Profile** through SteamVR / OpenXR
* XR Origin with controller ray interaction
* World Space Canvas UI
* XR UI Input Module
* Tracked Device Graphic Raycaster on VR-interactive canvases

Current controller-related functions include:

* controller ray UI interaction
* trigger-based memory anchor placement
* Marking / Exploration mode switching
* Line of Life panel toggle
* 3D timeline toggle
* preview marker distance control

Some controller bindings may depend on SteamVR controller configuration.

## Main Scene

```text
Assets/Scenes/SampleScene.unity
```

(Added to Build Settings.)

## Status

This is a thesis / lab prototype and is not production-ready.

Current development status:

* Core EchoSpace interaction flow is implemented.
* Vive Focus Vision integration is in progress.
* XR controller interaction is mostly working.
* UI, controller bindings, and device-specific behavior may still change.
* Large 3DGS assets are distributed via **Git LFS**.

---

## 快速開始（跨機開發）

### 1. Clone 專案（含 Submodule 與 LFS）

```bash
# 安裝 Git LFS（僅首次）
git lfs install

# Clone（建議一次帶入 submodule 與 LFS 物件）
git clone --recurse-submodules https://github.com/Hank00000802/EchoSpace.git
cd EchoSpace

# 若已 clone 但缺少 submodule / LFS 資料，補齊：
git submodule update --init --recursive
git lfs pull
```

### 2. 安裝 Unity

1. 安裝 [Unity Hub](https://unity.com/download)
2. 新增 Editor：**2022.3.62f3**
3. 建議模組：Windows Build Support (IL2CPP)、Visual Studio 或 Rider

### 3. 開啟專案

1. Unity Hub → **Open** → 選擇本專案根目錄
2. 首次開啟會自動還原 Package（含 VIVE Scoped Registry）
3. 確認 **Edit → Project Settings → Player → Rendering** 使用 **Direct3D12** 或 **Vulkan**
4. 開啟 `Assets/Scenes/SampleScene.unity`，按 **Play**

### 4. 設定 VR（HTC Vive）

1. **Edit → Project Settings → XR Plug-in Management → Standalone**：勾選 **OpenXR**
2. **OpenXR** 子項目：確認 **HTC Vive Controller Profile** 已啟用
3. 電腦需安裝 **SteamVR** 或 **VIVE 軟體**，頭顯追蹤正常後再 Play

---

## 專案結構與外部依賴

### Unity Package Manager（`Packages/manifest.json`）

| 套件 | 版本 | 備註 |
|------|------|------|
| [VIVE OpenXR Plugin](https://developer.vive.com/resources/openxr/unity/getting-started/) | 2.2.0 | Scoped Registry：`https://npm-registry.vive.com` |
| [XR Interaction Toolkit](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@2.6/manual/index.html) | 2.6.5 | Starter Assets 已納入 `Assets/Samples/` |
| [OpenXR Plugin](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.14/manual/index.html) | 1.14.3 | |
| [XR Management](https://docs.unity3d.com/Packages/com.unity.xr.management@4.5/manual/index.html) | 4.5.4 | |
| TextMeshPro | 3.0.7 | 已含於專案 |

### 3D Gaussian Splatting（Git Submodule）

本專案透過 **Git Submodule** 引用 [aras-p/UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting)：

```
External/UnityGaussianSplatting/   ← submodule（MIT）
Packages/manifest.json             ← file:../External/UnityGaussianSplatting/package
```

Clone 後若 Package Manager 報錯，執行：

```bash
git submodule update --init --recursive
```

### 材質貼圖

* 專案已內建 `Assets/Cartoon_Texture_Pack/`（可直接使用）
* 原始資產參考：[FREE Stylized PBR Textures Pack](https://assetstore.unity.com/packages/2d/textures-materials/free-stylized-pbr-textures-pack-111778)（L3Lumo-Art）
* 若出現 Normal Map 警告：將 `*_Normal.png` 的 **Texture Type** 設為 **Normal map**

---

## 開發備忘

| 功能 | 說明 |
|------|------|
| 主場景 | `Assets/Scenes/SampleScene.unity`（已加入 Build Settings） |
| Editor 高度調整 | `XRHeightAdjuster`：**Q/E** 調整、**Home** 重置 |
| 自訂腳本 | `Assets/Scripts/`（Line of Life、Annotation、SwitchView 等） |

---

## 常見問題

| 現象 | 處理方式 |
|------|----------|
| Package Manager 找不到 `org.nesnausk.gaussian-splatting` | `git submodule update --init --recursive` |
| 場景 Room 粉紅色 / Missing Reference | `git lfs pull`，確認 `Assets/GaussianAssets/` 完整 |
| `.bytes` 檔案很小（幾 KB）實際未下載 | 執行 `git lfs install` 後 `git lfs pull` |
| 3DGS 不顯示 | 改用 **DX12** 或 **Vulkan**，勿用 DX11 |
| VIVE 套件無法下載 | 確認 `manifest.json` 含 VIVE Scoped Registry |
| VR 無畫面 | 確認 OpenXR 已啟用、SteamVR/VIVE Runtime 執行中 |

---

## License

* EchoSpace project code: per repository license
* [UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting): MIT (splat training data may have separate academic/commercial restrictions)
* [FREE Stylized PBR Textures Pack](https://assetstore.unity.com/packages/2d/textures-materials/free-stylized-pbr-textures-pack-111778): Unity Asset Store Standard EULA
