# Lyra Viewer

[![Ko-fi](https://img.shields.io/badge/Ko--fi-Support%20Lyra-ff5e5b?logo=ko-fi)](https://ko-fi.com/nineveh_dev)

---

## Contents

- [Overview](#overview)
    - [What Lyra is and what it isn't](#what-lyra-is-and-what-it-isnt)
    - [Recommended hardware & known limitations](#recommended-hardware--known-limitations)
- [Key Features](#key-features)
- [Technical Details](#technical-details)
- [Supported Image Formats](#supported-image-formats)
    - [Common Raster Formats (Essential)](#common-raster-formats-essential)
    - [Modern / Web-Friendly Formats](#modern--web-friendly-formats)
    - [High Dynamic Range Formats](#high-dynamic-range-formats)
    - [GPU Formats](#gpu-formats)
    - [Document / Vector Formats](#document--vector-formats)
    - [Minor Formats](#minor-formats)
- [PSD / PSB Decoding Model](#psd--psb-decoding-model)
    - [PSD Color Mode Support](#psd-color-mode-support)
    - [PSB Support](#psb-support)
    - [ICC Color Profiles](#icc-color-profiles)
    - [Displayed PSD Information](#displayed-psd-information)
    - [Future Direction](#future-direction)
- [Keyboard Shortcuts & Controls](#keyboard-shortcuts--controls)
    - [macOS Specific](#macos-specific)
    - [Open With / Drag & Drop](#open-with--drag--drop)
- [Dependencies](#dependencies)
- [Installation](#installation)
    - [macOS (Homebrew)](#macos-homebrew)
    - [Linux](#linux)
- [Configuration & Data Files (UNIX specific)](#configuration--data-files-unix-specific)
    - [Configuration](#configuration)
    - [Data](#data)

## Overview

![Screenshot](docs/images/screenshot.png)

Lyra is a high-performance, minimalist image viewer designed for speed, fluid navigation, and precision.
It handles modern and professional image formats without the overhead of full editing suites or Electron-based tools.
Built for anyone who relies on images as a core resource in their workflow:

- 2D/3D artists and game developers browsing texture maps and baked assets
- Photographers reviewing large batches of exports
- Developers inspecting UI assets, icons, and generated output
- And ordinary advanced users

### What Lyra is and what it isn't

- Lyra is solely a viewer - nothing more. It opens and displays your files; it never writes to them, moves them, or
  deletes them. Your files are always safe and untouched.
- Lyra is not an Electron application. It is a native application built on SDL3 and Skia, with no embedded web
  browser and no JavaScript runtime. It runs on .NET 9 and renders directly through your GPU, keeping performance at the
  forefront of every decision.
- Lyra does not connect to the internet. It has no telemetry, no update pings, no cloud sync, and no AI features.
  Everything runs locally, offline, on your machine. Updates are manual - check for new releases and install them
  through your package manager when you're ready. If there's a format, workflow, or feature you'd like to see, the right
  place to say so is the [GitHub issue tracker](https://github.com/lyra-viewer/Lyra/issues).

### Recommended hardware & known limitations

Lyra is designed for capable, modern hardware - a dedicated GPU and SSD storage will get the best out of it. Not every
limitation is Lyra's to solve: network shares over SMB are constrained to a single stream by the protocol itself and
cannot be parallelised, so performance over a NAS or remote share will always be bounded by that ceiling.

---

## Key Features

- Fast navigation through large directories of images or texture assets.
- **Directory tree sidebar** for browsing the filesystem without leaving the viewer.
- **SVG** support for previewing scalable vector assets.
- **Adjustable background** modes to improve visibility of transparent images.
- **EXIF metadata** and format-specific information panel.
- **PSD layer hierarchy** panel showing group structure, layer names, and visibility state.
- Zoom-to-cursor and panning for intuitive inspection at any scale.
- Reasonable support for modern image formats, with limited support for older formats that refuse to die.

---

## Technical Details

Lyra is built on .NET 9 with SDL3 for windowing and input, and SkiaSharp for hardware-accelerated rendering via OpenGL or Metal.
It is not an Electron app - there is no embedded browser, no web runtime, and no hidden resource overhead (and definitely no AI client).
The architecture is designed around fast, non-blocking image loading:

- Decoded images are cached and adjacent files are preloaded in the background, so navigation feels instant even in large directories.
- Large PSD/PSB files use streaming and tiled decoding to avoid loading entire documents into memory - tested with files exceeding 3 GB.

Lyra integrates lightweight native interop wrappers for HDR, EXR, JPEG 2000, and TIFF decoding, and delegates format-specific work to focused libraries 
rather than bundling large native dependencies. Simpler formats such as TGA are handled by a small in-house managed decoder with no external dependency. 
System libraries like libheif, OpenJPEG, OpenEXR and libtiff are expected from the package manager (e.g. Homebrew).
Originally built for workflows involving texture maps, HDRIs, and assets exported from tools like Blender and Quixel Bridge - but the design 
generalizes well to any image-heavy workflow.

> _Developer note:_ Lyra is designed and written simultaneously.
> As a result, parts of the code reflect iterative exploration rather than a fully pre-planned architecture.
> Refactoring is ongoing wherever it improves clarity or maintainability.

---

## Supported Image Formats

### Common Raster Formats (Essential)

| Format      | Description                                      | Extensions                    |
|-------------|--------------------------------------------------|-------------------------------|
| PNG         | Lossless raster image format with optional alpha | `.png`                        |
| JPEG / JFIF | Lossy raster image format (JPEG family)          | `.jpg` `.jpeg` `.jif` `.jfif` |
| TIFF        | High-precision raster image container            | `.tif` `.tiff`                |
| Targa       | Raster image format with optional alpha          | `.tga`                        |
| BMP         | Uncompressed bitmap image format                 | `.bmp`                        |

### Modern / Web-Friendly Formats

| Format      | Description                                         | Extensions      |
|-------------|-----------------------------------------------------|-----------------|
| WebP        | Compressed raster image format with optional alpha  | `.webp`         |
| HEIF / HEIC | High-efficiency image container format (HEVC-based) | `.heif` `.heic` |
| AVIF        | High-efficiency image format based on AV1           | `.avif`         |

### High Dynamic Range Formats

| Format       | Description                                     | Extensions |
|--------------|-------------------------------------------------|------------|
| OpenEXR      | High-dynamic range, multi-channel raster format | `.exr`     |
| Radiance HDR | High-dynamic range RGBE format                  | `.hdr`     |

> _Note:_ EXR and HDR images are tone-mapped for display using the **ACES filmic** curve, so high-dynamic-range
> highlights roll off smoothly instead of clipping harshly to white.

### GPU Formats

| Format | Description                    | Extensions       |
|--------|--------------------------------|------------------|
| ~DDS~  | ~DirectDraw Surface~           | ~`.dds`~         |
| ~KTX~  | ~GPU texture container format~ | ~`.ktx` `.ktx2`~ |

### Document / Vector Formats

| Format    | Description              | Extensions    | Notes                                        |
|-----------|--------------------------|---------------|----------------------------------------------|
| SVG       | Scalable Vector Graphics | `.svg`        |                                              |
| Photoshop | Adobe Photoshop document | `.psd` `.psb` | See *PSD / PSB Decoding Model* section below |

### Minor Formats

| Format    | Description                   | Extensions                              | Notes                                                                                                                                                   |
|-----------|-------------------------------|-----------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------|
| ICO       | Icon container format         | `.ico`                                  |                                                                                                                                                         |
| ~ICNS~    | ~Apple icon container format~ | ~`.icns`~                               |                                                                                                                                                         |
| JPEG 2000 | Wavelet-based image format    | `.jp2` `.jpg2`<br/>`.j2k` `.j2c` `.jpc` | Lyra supports single-image JPEG 2000 files. Multi-image, animated, or compound JPEG 2000 formats (JPX, JPM, MJ2, JPIP) are intentionally NOT supported. |

> _Note:_ Crossed-out formats are not implemented yet.

---

## PSD / PSB Decoding Model

Lyra currently focuses on decoding the flattened **Image Data** section of Photoshop files, rather than individual
layers. This design choice prioritizes performance and fast previewing.

For PSD / PSB files, Lyra also surfaces the **layer hierarchy** in the sidebar - showing group structure, layer names,
and visibility state - independently of the flattened composite decode.

This is explicitly documented because the Image Data section is not strictly mandatory in the PSD specification and,
in some edge cases, may be missing or may not fully represent the document as it appears when opened in Photoshop.

![Photoshop file structure](docs/images/psd-file-structure.gif)

[Adobe Photoshop File Format Specification](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/PhotoshopFileFormats.htm#50577409_pgfId-1036097)

### PSD Color Mode Support

| Color Mode                   | Channels    | Lyra Support             |
|------------------------------|-------------|--------------------------|
| Bitmap                       | 1 (1-bit)   | Planned                  |
| Grayscale                    | 1           | Full                     |
| Duotone / Tritone / Quadtone | 1 + inks    | In progress (clean-room) |
| Indexed                      | 1 + palette | Full                     |
| RGB                          | 3           | Full                     |
| CMYK                         | 4           | Full                     |
| Lab                          | 3           | MVP                      |
| Multichannel                 | N           | In progress (clean-room) |

> _Legal Note:_ Duotone and Multichannel support is an independent, clean-room implementation. It was derived by
> observing the documented PSD/PSB file structure, publicly available format references, and the contents of sample
> files - not by decompiling, disassembling, or otherwise reverse-engineering Adobe software, and not from any Adobe
> source code. Spot/named colors are rendered using the color values stored within each document; no proprietary color
> libraries (e.g. PANTONE) are bundled.

### PSB Support

Lyra fully supports PSB (Photoshop Big Document Format) files.

- Successfully tested with ~3 GB PSB files
- Uses streaming / tiled decoding internally where possible to avoid loading entire images eagerly

![PSB Large](docs/images/psd-large.png)

### ICC Color Profiles

Lyra honors embedded ICC color profiles whenever they are present.
If a PSD / PSB document does not contain an embedded profile - most notably in CMYK color modes - Lyra falls back to
the system’s default color profile to produce a usable result.

Without an explicit ICC profile, CMYK data has no well-defined color meaning.
In such cases, different viewers may interpret the same document very differently, sometimes resulting in
severely distorted or inverted-looking colors.

Lyra’s fallback behavior is intended to be predictable and standards-compliant rather than attempting
heuristic or hard-coded CMYK assumptions.

> _Developer note:_ During development, Lyra was tested against several large CMYK PSB files from the NASA public image
> archive.
> These documents did not contain embedded ICC profiles and produced drastically different results across common
> image viewers - ranging from heavily shifted colors to near-inverted appearances.
>
> This behavior is not a defect of the files themselves, but a direct consequence of CMYK data being interpreted
> without a defined color profile.

### Displayed PSD Information

When viewing a PSD or PSB file, Lyra surfaces document-level metadata and the full layer hierarchy through dedicated
sidebar sections. This information is extracted directly from the binary file structure during decoding.

**PSD Layers**

The **PSD Layers** section presents the full layer hierarchy as a tree view, reconstructed from the flat layer record
list stored in the file. Groups are displayed with their child count and can be expanded or collapsed.

This display is read-only and independent of the flattened composite decode - Lyra does not render individual
layer contents, but provides the structural overview that is otherwise only visible inside Photoshop.

<img src="docs/images/psd-gui-example.png" width="400">

### Future Direction

The PSD decoder is intentionally structured to allow future expansion.

---

## Keyboard Shortcuts & Controls

| Key                   | Action                                            |
|-----------------------|---------------------------------------------------|
| `←` `→`               | Previous / Next image                             |
| `Home` `End`          | First / Last image                                |
| `+` `-`               | Zoom in / Zoom out                                |
| `Mouse Wheel`         | Zoom at cursor position                           |
| `Middle Mouse Button` | Customizable (see `app-settings.toml`)            |
| `0`                   | Toggle **Fit to Screen** / **Original Size**      |
| `S`                   | Toggle sampling mode                              |
| `F`                   | Toggle fullscreen                                 |
| `B`                   | Toggle background mode                            |
| `I`                   | Toggle image information overlay                  |
| `H`                   | Toggle help bar                                   |
| `Return`              | Reveal image or directory in native file explorer |
| `Esc`                 | Exit application                                  |

### macOS Specific

| Key         | Action                                  |
|-------------|-----------------------------------------|
| `⌘ ←` `⌘ →` | First / Last image                      |
| `⌥ ←` `⌥ →` | First / Last image within the directory |

### Open With / Drag & Drop

| Context                                | How Lyra interprets it                   | Make a collection from files around | Recursion |
|----------------------------------------|------------------------------------------|-------------------------------------|-----------|
| Single file                            | Anchor (Open / Open With / Double-click) | Yes                                 | No        |
| Multiple files (same directory)        | Selection                                | No                                  | No        |
| Single directory                       | Directory collection                     | No                                  | Yes       |
| Multiple directories                   | Multi-directory selection                | No                                  | Yes       |
| Mixed files from different directories | Multi-directory selection                | No                                  | No        |

> Recursion applies only when directories are explicitly dropped.
> Opening or dropping files never implicitly expands into subdirectories.

> _Developer note:_ Lyra intentionally favors context-aware navigation.
> Opening a single image always implies “show me this image in relation to its neighbors”, not isolation.

---

## Dependencies

| Library           | Purpose                                                                | License       | Repository                                                        |
|-------------------|------------------------------------------------------------------------|---------------|-------------------------------------------------------------------|
| SDL3-CS           | Core graphics, input, and windowing                                    | zlib          | [github](https://github.com/edwardgushchin/SDL3-CS)               |
| SkiaSharp         | Hardware-accelerated 2D rendering                                      | BSD-3-Clause  | [github](https://github.com/mono/SkiaSharp)                       |
| Svg.Skia          | SVG parsing and rendering                                              | MIT           | [github](https://github.com/wieslawsoltes/Svg.Skia)               |
| LibHeifSharp      | HEIF / HEIC image decoding                                             | LGPL-3.0      | [github](https://github.com/0xC0000054/libheif-sharp)             |
| OpenEXR           | High-dynamic-range OpenEXR (.exr) decoding                             | BSD-3-Clause  | [github](https://github.com/AcademySoftwareFoundation/openexr)    |
| rgbe              | Radiance HDR (.hdr) image decoding                                     | Public Domain | [webpage](https://www.graphics.cornell.edu/~bjw/rgbe.html)        |
| OpenJPEG          | JPEG 2000 still-image decoding                                         | BSD-2-Clause  | [github](https://github.com/uclouvain/openjpeg)                   |
| libtiff           | TIFF decoding                                                          | BSD-like      | [gitlab](https://gitlab.com/libtiff/libtiff)                      |
| Unicolour         | Color space conversions & perceptual color math (used in PSD decoding) | MIT           | [github](https://github.com/waacton/Unicolour)                    |
| MetadataExtractor | EXIF metadata extraction                                               | Apache 2.0    | [github](https://github.com/drewnoakes/metadata-extractor-dotnet) |

---

## Installation

Lyra Viewer is distributed via **Homebrew** on macOS.

### macOS (Homebrew)

```sh
brew tap lyra-viewer/lyra
brew install --cask lyra-viewer
```

### Linux

Not available yet.

---

## Configuration & Data Files (UNIX specific)

Lyra stores configuration and runtime data in standard XDG-compliant locations.

### Configuration

```~/.config/lyra-viewer/```

| File                | Description                                                                               |
|---------------------|-------------------------------------------------------------------------------------------|
| `app-settings.toml` | Application settings: renderer, window state, middle mouse button function, text sizes... |
| `ui-settings.toml`  | UI state - saved automatically on exit                                                    |

### Data

```~/.local/share/lyra-viewer/```

| File                  | Description                                                            |
|-----------------------|------------------------------------------------------------------------|
| `log.txt`             | Application log output                                                 |
| `load-time-data.toml` | Recorded decode times per format, used to estimate loading progress    |

If any configuration file is missing or malformed, Lyra falls back to built-in defaults and recreates the file on next save.
Deleting everything under these directories is always safe - Lyra will start fresh with default settings.

---