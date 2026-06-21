# EchoSpace

A Unity VR research prototype for **EchoSpace** — a personalized reflective space that combines **3D Gaussian Splatting (3DGS)** reconstructed environments with **identity-integration** activities, including memory marking, life-timeline organization, guided reflection, and perspective switching.

EchoSpace is developed as a thesis / lab prototype for exploring how immersive personal spaces can support self-continuity and reflection during life transitions.

## Contents

* [Overview](#overview)
* [Current Features](#current-features)
* [VR Interaction Flow](#vr-interaction-flow)
* [What's in this repo](#whats-in-this-repo)
* [Not included](#not-included)
* [Requirements](#requirements)
* [XR Setup Notes](#xr-setup-notes)
* [Main Scene](#main-scene)
* [Status](#status)
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

## Not included

Large **3DGS splat `.bytes` files** under:

```text
Assets/GaussianAssets/
```

are gitignored because they are too large for GitHub.

After cloning the repo, place exported splat assets locally under `Assets/GaussianAssets/`.
The main scene references room splats from this folder.

## Requirements

* Unity project with:

  * OpenXR
  * XR Interaction Toolkit 2.6.x
  * Input System
  * TextMeshPro
* VR headset and controllers

  * Current target: **HTC Vive Focus Vision via SteamVR**
* [UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting)

  * Update the local package path in `Packages/manifest.json` if needed.

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

## Status

This is a thesis / lab prototype and is not production-ready.

Current development status:

* Core EchoSpace interaction flow is implemented.
* Vive Focus Vision integration is in progress.
* XR controller interaction is mostly working.
* UI, controller bindings, and device-specific behavior may still change.
* Large 3DGS assets are managed locally and are not included in the repository.

## License

TBD
