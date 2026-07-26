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
- [DDS & KTX Texture Decoding Model](#dds--ktx-texture-decoding-model)
    - [Supported Texture Formats](#supported-texture-formats)
    - [Containers](#containers)
    - [Color & Signedness](#color--signedness)
    - [File Inspector](#file-inspector)
    - [Safety](#safety)
    - [Not Yet Supported](#not-yet-supported)
- [Keyboard Shortcuts & Controls](#keyboard-shortcuts--controls)
    - [macOS Specific](#macos-specific)
    - [Open With / Drag & Drop](#open-with--drag--drop)
- [Dependencies](#dependencies)
- [Native Libraries & Bundling](#native-libraries--bundling)
- [Installation](#installation)
    - [macOS (Homebrew)](#macos-homebrew)
    - [Windows (Scoop)](#windows-scoop)
    - [Linux (APT)](#linux-apt)
    - [Linux (direct .deb)](#linux-direct-deb)
- [Configuration & Data Files](#configuration--data-files)
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
- Lyra does not connect to the internet. It has no telemetry, no update pings, no cloud sync, no nag screens, and no AI
  features. Everything runs locally, offline, on your machine. Updates are manual - check for new releases and install
  them through your package manager (Homebrew, APT, or Scoop) when you're ready. If there's a format, workflow, or
  feature you'd like to see, the right place to say so is the [GitHub issue tracker](https://github.com/lyra-viewer/Lyra/issues).

### Recommended hardware & known limitations

Lyra is designed for capable, modern hardware - a dedicated GPU and SSD storage will get the best out of it. Not every
limitation is Lyra's to solve: network shares over SMB are constrained to a single stream by the protocol itself and
cannot be parallelised, so performance over a NAS or remote share will always be bounded by that ceiling.

---

## Key Features

- Fast navigation through large directories of images or texture assets.
- Zoom-to-cursor and panning for intuitive inspection at any scale.
- **Directory tree sidebar** for browsing the filesystem without leaving the viewer.
- **SVG** support for previewing scalable vector assets.
- **Adjustable background** modes to improve visibility of transparent images.
- **Duplicate finder** that locates exact and visually similar images across a directory tree using perceptual hashing.
- **EXIF metadata** and **format-specific** information panel.
- **PSD layer hierarchy** panel showing group structure, layer names, and visibility state.
- Reasonable support for modern image formats, with limited support for older formats that refuse to die.

---

## Technical Details

Lyra is built on .NET 9 with SDL3 for windowing and input, and SkiaSharp for hardware-accelerated rendering via OpenGL or Metal.
It is not an Electron app - there is no embedded browser, no web runtime, and no hidden resource overhead (and definitely no AI client).
The architecture is designed around fast, non-blocking image loading:

- Decoded images are cached and adjacent files are preloaded in the background, so navigation feels instant even in large directories.
- Large PSD/PSB files use streaming and tiled decoding to avoid loading entire documents into memory - tested with files exceeding 3 GB.

Decoding is split into two layers. **Lyra.ManagedCodecs** is a pure-managed, dependency-free codec library that
owns the formats Lyra decodes itself - TGA, Radiance HDR, and the GPU texture containers (DDS, KTX, KTX2) together
with their block formats (BC1–BC7, BC6H, ETC2 / EAC, ASTC). These readers parse the container structure in C#,
slice each subresource as a zero-copy view into the source file, treat all input as hostile (every byte range and
surface size is bounds-checked against overflow), and decode only the surface actually needed - so a thumbnail or a
perceptual hash never pays to decode a full-resolution mip. Because nothing here links a native library, it behaves
identically on every platform .NET targets.

For the remaining formats Lyra integrates lightweight native interop wrappers for EXR, JPEG 2000, JPEG XL, and TIFF
decoding, delegating format-specific work to focused libraries. The one native exception inside the managed codec layer
is **Basis Universal** (ETC1S / UASTC) supercompression carried in KTX2: rather than reimplement its intricate
transcoder, Lyra wraps Binomial's open-source reference transcoder (Apache-2.0) in a small native wrapper.

How these native libraries are shipped differs by platform - see [Native Libraries & Bundling](#native-libraries--bundling).

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

| Format      | Description                                         | Extensions      | Notes                                                                                                                                                                 |
|-------------|-----------------------------------------------------|-----------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| AVIF        | High-efficiency image format based on AV1           | `.avif`         |                                                                                                                                                                       |
| HEIF / HEIC | High-efficiency image container format (HEVC-based) | `.heif` `.heic` |                                                                                                                                                                       |
| JPEG XL     | JPEG XL Image Coding System                         | `.jxl`          | Lyra displays static JPEG XL images. Animated JXL is decoded to its first frame only (same policy as JPEG 2000). HDR (floating-point) JXL is tone-mapped for display. |
| WebP        | Compressed raster image format with optional alpha  | `.webp`         |                                                                                                                                                                       |

### Document / Vector Formats

| Format    | Description              | Extensions    | Notes                                        |
|-----------|--------------------------|---------------|----------------------------------------------|
| SVG       | Scalable Vector Graphics | `.svg`        |                                              |
| Photoshop | Adobe Photoshop document | `.psd` `.psb` | See *PSD / PSB Decoding Model* section below |

### High Dynamic Range Formats

| Format       | Description                                     | Extensions |
|--------------|-------------------------------------------------|------------|
| OpenEXR      | High-dynamic range, multi-channel raster format | `.exr`     |
| Radiance HDR | High-dynamic range RGBE format                  | `.hdr`     |

> _Note:_ EXR and HDR images are tone-mapped for display using the **ACES filmic** curve, so high-dynamic-range
> highlights roll off smoothly instead of clipping harshly to white.

### GPU Formats

| Format | Description                   | Extensions     | Notes                                                |
|--------|-------------------------------|----------------|------------------------------------------------------|
| DDS    | DirectDraw Surface            | `.dds`         | See *DDS & KTX Texture Decoding Model* section below |
| KTX    | Khronos GPU texture container | `.ktx` `.ktx2` | KTX 1.x and KTX 2.0; see section below               |

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

## DDS & KTX Texture Decoding Model

DDS and KTX are GPU texture containers - one file can hold a full mip chain, cube-map faces, array layers, or
volume slices, usually in a block-compressed GPU format. Lyra reads all three (`.dds`, `.ktx`, `.ktx2`) with a
single **pure-managed** codec (no native dependencies, save the Basis transcoder noted below): for display it
decodes the base surface (mip 0, first face / layer); for thumbnails and perceptual hashing it decodes the
*smallest stored mip that still covers the target size*. The container readers differ - each owns its own header
and format mapping - but they all feed one shared set of block decoders.

### Supported Texture Formats

| Family                 | Formats                                                                                      | Notes                               |
|------------------------|----------------------------------------------------------------------------------------------|-------------------------------------|
| Block-compressed (BCn) | BC1–BC3 (DXT1/3/5), BC4 / BC5 (unorm + snorm), BC7                                           | The mainstream desktop formats      |
| HDR block              | BC6H (signed + unsigned)                                                                     | Decoded to float, then tone-mapped  |
| Mobile block           | ETC2 / EAC - RGB, RGB+A1, RGBA8, R11 / RG11 (unorm + snorm)                                  | Typically carried in KTX / KTX2     |
| Adaptive block (ASTC)  | All LDR footprints - 2D (4×4 … 12×12) and 3D (3×3×3 … 6×6×6)                                 | sRGB + linear; HDR ASTC not decoded |
| Uncompressed 8-bit     | R8, RG8, RGB8, RGBA8 / BGRA8 (+ sRGB), `snorm`, packed (4/4/4/4, 5/6/5, 5/5/5/1, 10/10/10/2) | `snorm` remapped for display        |
| Uncompressed float     | R16F / R32F, RGB16F, RGBA16F / RGBA32F, RG11B10, RGB9E5                                      | Decoded to float, then tone-mapped  |

The decoders are validated against independent reference decoders - BC1 / BC3 / BC7 against Pillow, BC6H against
`imagecodecs`, and ASTC against the official **astcenc** reference decoder - across fuzzed inputs covering every
block mode and partition.

### Containers

- **DDS** - both the legacy `DDS_PIXELFORMAT` header and the `DX10` extended header, including four-character
  codes (`DXT1`, `ATI2`, `BC5S`, …), the numeric `D3DFORMAT` codes some older D3D9 exporters store in the FourCC
  field, and `DXGI_FORMAT` identifiers.
- **KTX 1.x** - mapped from its OpenGL `glInternalFormat`. Rare big-endian files are byte-swapped on read, and the
  OpenGL bottom-left row order is flipped to top-left for display.
- **KTX 2.0** - mapped from its Vulkan `VkFormat`. Per-level **Zstandard** and **ZLIB** supercompression is
  inflated on read. **Basis Universal** (ETC1S / UASTC) payloads are transcoded to RGBA by a small native wrapper -
  the one native dependency in this path - since their block stream is proprietary.

### Color & Signedness

Decoding is **faithful** - no color transform is applied, so an sRGB source decodes to sRGB-tagged bytes and the
display path linearizes.

- **HDR formats** (BC6H, RGBA16F / RGBA32F, and the packed float formats) are scene-referred float and are
  tone-mapped with the same **ACES filmic** curve used for EXR and Radiance HDR, so highlights roll off smoothly
  instead of clipping.
- **Signed (`snorm`) formats** - common in bump / normal maps - are remapped from `[-1, 1]` to `[0, 1]`, which
  avoids the "shifted color" look some viewers produce by rendering the raw signed bytes as unsigned.

### File Inspector

When a DDS or KTX file is open, two sidebar sections surface its internals:

- **Format Specific** lists the headline facts: the source-native format name (e.g. `BC7_UNORM`, `DXT4`, a Vulkan
  `VK_FORMAT_…` for KTX2, or `R16G16B16A16_FLOAT (FourCC 'q')` when a numeric `D3DFORMAT` is decoded), *Has Alpha*,
  *Is Cubemap*, *Is Volume*, *Depth* (volumes only), *Mipmap Count*, and *Bits/Pixel*.
- **Structure** is a scrollable, collapsible view of the file's binary layout - the container header and its
  sub-structures, and every mip level. Each part shows its name, a short description and its byte size, and expands
  to the raw key-value fields it holds.

### Safety

Lyra treats texture input as hostile. It parses the full subresource layout (mips, faces, array layers, volume
depth slices) and validates every subresource's byte range against the file length before exposing it;
dimensions, mip counts and surface sizes are bounds-checked against overflow, and each parsed level is cross-checked
against Lyra's own independent sizing math. A malformed or truncated header is rejected cleanly rather than read out
of bounds, and an unrecognized format fails with a descriptive message naming the exact `DXGI_FORMAT`, `VkFormat`,
`glInternalFormat`, or FourCC rather than failing silently.

### Not Yet Supported

- **PVRTC** - the PowerVR block formats are not decoded.
- **HDR ASTC** - LDR ASTC is fully supported; the HDR ASTC profiles are not yet decoded.
- KTX files that declare zero stored levels (deferred runtime mip generation) are rejected rather than guessed.

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
| `Esc`                 | Cancel an operation, or exit application          |

### macOS Specific

| Key         | Action                                  |
|-------------|-----------------------------------------|
| `⌘ ←` `⌘ →` | First / Last image                      |
| `⌥ ←` `⌥ →` | First / Last image within the directory |

### Windows / Linux Specific

| Key               | Action                                  |
|-------------------|-----------------------------------------|
| `Ctrl ←` `Ctrl →` | First / Last image within the directory |

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

| Library           | Purpose                                                                                    | License      | Repository                                                        |
|-------------------|--------------------------------------------------------------------------------------------|--------------|-------------------------------------------------------------------|
| SDL3-CS           | Core graphics, input, and windowing                                                        | zlib         | [github](https://github.com/edwardgushchin/SDL3-CS)               |
| SkiaSharp         | Hardware-accelerated 2D rendering                                                          | BSD-3-Clause | [github](https://github.com/mono/SkiaSharp)                       |
| Svg.Skia          | SVG parsing and rendering                                                                  | MIT          | [github](https://github.com/wieslawsoltes/Svg.Skia)               |
| LibHeifSharp      | HEIF / HEIC image decoding                                                                 | LGPL-3.0     | [github](https://github.com/0xC0000054/libheif-sharp)             |
| OpenEXR           | High-dynamic-range OpenEXR (.exr) decoding                                                 | BSD-3-Clause | [github](https://github.com/AcademySoftwareFoundation/openexr)    |
| OpenJPEG          | JPEG 2000 still-image decoding                                                             | BSD-2-Clause | [github](https://github.com/uclouvain/openjpeg)                   |
| libjxl            | JPEG XL decoding (native wrapper)                                                          | BSD-3-Clause | [github](https://github.com/libjxl/libjxl)                        |
| libtiff           | TIFF decoding                                                                              | BSD-like     | [gitlab](https://gitlab.com/libtiff/libtiff)                      |
| Basis Universal   | KTX2 ETC1S / UASTC transcoding (native wrapper)                                            | Apache-2.0   | [github](https://github.com/BinomialLLC/basis_universal)          |
| ZstdSharp.Port    | Zstandard decompression for KTX2 supercompression                                          | MIT          | [github](https://github.com/oleg-st/ZstdSharp)                    |
| Unicolour         | Color space conversions & perceptual color math (transitive, via the in-house PSD decoder) | MIT          | [github](https://github.com/waacton/Unicolour)                    |
| MetadataExtractor | EXIF metadata extraction                                                                   | Apache-2.0   | [github](https://github.com/drewnoakes/metadata-extractor-dotnet) |
| Tomlyn            | TOML parsing for configuration files                                                       | BSD-2-Clause | [github](https://github.com/xoofx/Tomlyn)                         |
| System.IO.Hashing | Fast non-cryptographic hashing (duplicate detection)                                       | MIT          | [github](https://github.com/dotnet/runtime)                       |

---

## Native Libraries & Bundling

A handful of formats are decoded through native libraries (libheif, OpenJPEG, libjxl, OpenEXR, libtiff, plus the Basis
Universal transcoder). How those libraries are delivered depends on the platform:

- **macOS** - expected from the package manager (Homebrew).
- **Linux** - resolved as APT dependencies of the `.deb`, with the exception of **libjxl**, which is vendored inside the
  package for now. This is a temporary measure until JPEG XL support is more widely available across Ubuntu releases; it
  will be dropped in favor of the system package once that lands.
- **Windows** - bundled with the application. The wrapper DLLs are self-contained and ship inside the distribution, so no
  separate installation is required.

---

## Installation

Lyra Viewer is distributed via **Homebrew** on macOS, an **APT repository** (or a
direct `.deb`) on Debian/Ubuntu, and **Scoop** on Windows.

### macOS (Homebrew)

```sh
brew tap lyra-viewer/lyra
brew trust --cask lyra-viewer/lyra/lyra-viewer
brew install --cask lyra-viewer
```

### Windows (Scoop)

```sh
scoop bucket add lyra-viewer https://github.com/lyra-viewer/scoop-lyra
scoop install lyra-viewer
```

Updates then arrive through `scoop update lyra-viewer`.

### Linux (APT)

Add the signed repository once, then install and receive updates through `apt`:

```sh
# 1. Trust the repository signing key
sudo mkdir -p /etc/apt/keyrings
curl -fsSL https://lyra-viewer.github.io/apt-lyra/lyra-archive-keyring.asc \
  | sudo gpg --dearmor -o /etc/apt/keyrings/lyra.gpg

# 2. Add the repository
echo "deb [signed-by=/etc/apt/keyrings/lyra.gpg] https://lyra-viewer.github.io/apt-lyra stable main" \
  | sudo tee /etc/apt/sources.list.d/lyra.list

# 3. Install
sudo apt update
sudo apt install lyra-viewer
```

Updates then arrive through the usual `sudo apt update && sudo apt upgrade`.

To remove the repository:

```sh
sudo rm /etc/apt/sources.list.d/lyra.list /etc/apt/keyrings/lyra.gpg
sudo apt update
```

### Linux (direct .deb)

Prefer not to add a repository? Download `lyra-viewer_<version>_amd64.deb` from the
[latest release](https://github.com/lyra-viewer/Lyra/releases/latest) and install it
directly (`apt` resolves the system dependencies):

```sh
sudo apt install ./lyra-viewer_0.5.1_amd64.deb
```

> _Note:_ Linux builds are **amd64 (x86-64)** only for now.

---

## Configuration & Data Files

On macOS and Linux, Lyra stores configuration and runtime data in standard XDG-compliant locations. On Windows, both the
configuration and data files live together under `%LOCALAPPDATA%\lyra-viewer` (the file names below are unchanged).

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
