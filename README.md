# RadiusXtract

> **Automated, GPU-accelerated 3D point cloud cylinder fitting and radius estimation for industrial pipelines.**

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue)](https://www.microsoft.com/windows)
[![Framework](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WinUI%203%20%2F%20Windows%20App%20SDK-0078D7)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Graphics](https://img.shields.io/badge/Viewport-Direct3D%20%2F%20WebGL%202.0-brightgreen)](https://developer.mozilla.org/en-US/docs/Web/API/WebGL_API)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

---

## Overview

**RadiusXtract** is a high-performance Windows desktop application designed to inspect 3D LiDAR and laser point cloud scans of industrial pipes. It automatically identifies cylindrical geometry, isolates the true pipe surface from background noise, and accurately computes the outer radius, diameter, length, orientation vector, and inclination angle in real time.

---

## Features

- **Automated Cylinder Fitting:** Instantly calculates pipe radius, diameter, length, and spatial orientation from `.pcd` (ASCII / Binary v0.7), `.ply`, and `.xyz` files.
- **Mathematically Rigorous Estimation Pipeline:**
  - **PCA (Principal Component Analysis):** Computes the 3D spatial covariance matrix to determine the dominant pipe cylinder axis vector.
  - **Taubin Algebraic Circle Fit:** Fits projected 2D arc data with minimal algebraic distance, completely free of the curvature bias common in traditional Kåsa circle fits.
  - **RANSAC Consensus:** Iteratively weeds out false inliers, noise points, and sensor jitter to maximize surface consensus and minimize RMS residual error.
- **Hardware-Accelerated 3D Viewport:** Sustained 60–120+ FPS orbit, pan, and zoom powered by your dedicated GPU (NVIDIA GeForce RTX / Direct3D 11 via WebView2).
- **Interactive Visual Layers & Shaders:**
  - **Color Modes:** Dynamic Axis (XYZ gradient), Depth (Turbo heatmap), and Solid (industrial cyan).
  - **Outlier Detection:** Highlights noisy and rejected points in neon red (`#FF2626`) with dynamic 1.6× sprite enlargement.
  - **3D Fitted Parametric Wireframe:** Overlays bounding cylinder rings, longitudinal surface lines, and central axis.
  - **3D CAD Measurement Grid:** Toggleable ground reference grid.
  - **Dynamic Point Sizing:** 1 px to 16 px slider adjustment with smooth bilinear disc anti-aliasing.
- **Dimensional Unit Switching:** Instant live conversion across Millimeters (`mm`), Meters (`m`), and Inches (`in`).
- **Inspection Reporting:** One-click generation and export of technical inspection certificates to clipboard or `.txt`.

---

## System Prerequisites

Before running the application, ensure your system has:

1. **Operating System:** Windows 10 (version 19041+) or Windows 11 (x64).
2. **.NET SDK:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) installed.
3. **Graphics Card:** Direct3D 11 / WebGL 2.0 capable GPU (NVIDIA GeForce RTX recommended for peak performance).
4. **WebView2 Runtime:** Pre-installed with Windows 10/11 (or free download via [Microsoft WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)).

---

## How to Run

### Method 1: Using Visual Studio 2022 (Recommended for Developers)

You do **not** need Git installed to run this project:

1. **Get the source code:**
   - **Option A (Download ZIP):** On GitHub, click the green **`Code`** button $\rightarrow$ click **`Download ZIP`**. Extract the `.zip` to any folder on your PC.
   - **Option B (Git Clone):**
     ```bash
     git clone https://github.com/SohamB-42/RadiusXtractUI.git
     ```
2. **Open the Solution:**
   - Double-click **`RadiusXtractUI.sln`** inside the folder to open it in **Visual Studio 2022** (v17.8 or newer).
   - *(Note: Ensure the **.NET desktop development** workload and **Windows App SDK** are installed in your Visual Studio installer).*
3. **Run:**
   - In the top toolbar, set configuration to **`Debug`** (or `Release`) and platform to **`x64`**.
   - Press <kbd>F5</kbd> (or click the green **Start** button). Visual Studio will automatically restore NuGet packages, compile the shaders, and launch the application.

---

### Method 2: Using the .NET CLI (Terminal / PowerShell)

1. Open PowerShell or Command Prompt in the project folder:
   ```bash
   cd RadiusXtractUI
   ```
2. Build and launch:
   ```bash
   dotnet build -c Debug
   dotnet run -c Debug
   ```

---

## How to Use & Test

1. **Launch the application.**
2. Click **Open** (or drop a file into the import area) and select any point cloud file (e.g., `pipe1.pcd`).
3. **3D Viewport Controls:**
   - **Left Click + Drag:** Orbit / rotate 3D view.
   - **Right Click + Drag:** Pan camera horizontally / vertically.
   - **Scroll Wheel:** Zoom in and out.
   - **Reset View:** Press the camera reset button to center the pipe.
4. **Inspect Pipeline Metrics:** The right-hand telemetry card instantly displays:
   - **Estimated Radius & Diameter** (with active confidence score)
   - **Pipe Length**
   - **Orientation Vector** $(\hat{x}, \hat{y}, \hat{z})$ and **Inclination Angle**
   - **Inlier vs. Outlier Counts** and RMS surface error
5. **Toggle Display Modes:**
   - Switch between **Axis (XYZ)**, **Depth**, and **Solid** colormaps.
   - Toggle **Show outliers** to inspect noise points filtered out by RANSAC.
   - Toggle **Fitted cylinder** to see the parametric cylinder model superimposed on the raw scan.
   - Toggle **Reference grid** for spatial ground context.
6. **Export Findings:** Click **Export report** to copy or save a formatted inspection summary.

---

## Project Architecture

```text
RadiusXtractUI/
├── RadiusXtractUI.sln           # Visual Studio solution file
├── RadiusXtractUI.csproj        # .NET 10 / WindowsAppSDK project configuration
├── Assets/
│   ├── pointcloud_viewer.html   # WebGL2 / Direct3D hardware-accelerated shader engine
│   └── AppIcon.ico              # Application branding asset
├── Helpers/
│   ├── CylinderFitter.cs        # PCA covariance, Taubin circle fit, & RANSAC consensus
│   ├── PcdReader.cs             # Binary & ASCII PCD v0.7 parser
│   ├── PointCloudRenderer.cs    # Software rasterizer fallback
│   └── UnitConverter.cs         # Live mm / m / inches conversion engine
├── Models/
│   ├── PipeResult.cs            # Dimensional & statistical fit data model
│   └── StubData.cs              # Default placeholder states
├── ViewModels/
│   ├── EstimateViewModel.cs     # MVVM state, commands, and unit binding
│   └── BaseViewModel.cs         # ObservableObject base
├── Views/
│   ├── EstimatePage.xaml(.cs)   # Primary estimation view & GPU WebView2 bridge
│   ├── BatchPage.xaml(.cs)      # Multi-file batch processing view
│   ├── HistoryPage.xaml(.cs)    # Inspection history database logs
│   └── SettingsPage.xaml(.cs)   # Visual & unit preferences
├── MainWindow.xaml(.cs)         # Main navigation rail and shell
└── README.md                    # Project documentation
```

---

## License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.
