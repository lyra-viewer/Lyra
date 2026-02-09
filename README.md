# Lyra Viewer

---

## Overview

Lyra is a high-performance, minimalist image viewer designed for speed, fluid navigation, and precision, ideal for
creative professionals who rely on images as a core resource in their workflow — such as:

- 2D/3D artists
- Game developers
- Environment designers
- And other advanced users

Built on SDL3 and SkiaSharp, Lyra is optimized for browsing collections of texture maps, HDRIs, baked assets, and
other images exported from tools like Blender, Quixel Bridge or modern DCC pipelines.

> _Developer note:_ Lyra is designed and written simultaneously.
> As a result, parts of the code reflect iterative exploration rather than a fully pre-planned architecture.
> Refactoring is ongoing wherever it improves clarity or maintainability.

---

## Key Features

- Fast navigation through large directories of images or texture assets.
- **SVG** support for previewing scalable vector assets.
- **Adjustable background** modes to improve visibility of transparent images
- Sampling toggle, useful for **pixel-perfect** graphics or UI assets.
- **EXIF metadata** display for viewing embedded image information.
- Zoom-to-cursor and panning for intuitive inspection at any scale.
- Reasonable support for modern image formats, with limited support for older formats that refuse to die.

---

## Supported Image Formats

> _Note:_ Crossed-out formats are not implemented yet.

### Common Raster Formats (Essential)

| Format       | Description                                        | Extensions                    |
| ------------ | -------------------------------------------------- | ----------------------------- |
| PNG          | Lossless raster image format with optional alpha   | `.png`                        |
| JPEG / JFIF  | Lossy raster image format (JPEG family)            | `.jpg` `.jpeg` `.jif` `.jfif` |
| TIFF         | High-precision raster image container              | `.tif` `.tiff`                |
| Targa        | Raster image format with optional alpha            | `.tga`                        |
| BMP          | Uncompressed bitmap image format                   | `.bmp`                        |

### Modern / Web-Friendly Formats

| Format       | Description                                         | Extensions               |
| ------------ | --------------------------------------------------- | ------------------------ |
| WebP         | Compressed raster image format with optional alpha  | `.webp`                  |
| HEIF / HEIC  | High-efficiency image container format (HEVC-based) | `.heif` `.heic`          |
| AVIF         | High-efficiency image format based on AV1           | `.avif`                  |

### High Dynamic Range Formats

| Format       | Description                                           | Extensions               |
| ------------ | ----------------------------------------------------- | ------------------------ |
| OpenEXR      | High-dynamic range, multi-channel raster format       | `.exr`                   |
| Radiance HDR | High-dynamic range RGBE format                        | `.hdr`                   |

### GPU Formats

| Format      | Description                                         | Extensions          |
| ----------- | --------------------------------------------------- | ------------------- |
| ~DDS~       | ~DirectDraw Surface~                                | `.dds`              |
| ~KTX~       | ~GPU texture container format~                      | `.ktx` `.ktx2`      |

### Document / Vector Formats

| Format       | Description                                         | Extensions                |
| ------------ | --------------------------------------------------- | ------------------------- |
| SVG          | Scalable Vector Graphics                            | `.svg`                    |
| Photoshop    | Adobe Photoshop document                            | `.psd` `.psb`             |

### Minor Formats

| Format         | Description                                         | Extensions             |
| -------------- | --------------------------------------------------- | ---------------------- |
| ICO            | Icon container format                               | `.ico`                 |
| ~ICNS~         | ~Apple icon container format~                       | `.icns`                |
| ~JPEG 2000~    | ~Wavelet-based image format~                        | `.jp2` `.j2k` `.jpx`   |

---

## PSD / PSB Decoding Model

Lyra currently focuses on decoding the flattened **Image Data** section of Photoshop files, rather than individual layers.
This design choice prioritizes performance, and fast previewing.

This is explicitly documented because the Image Data section is not strictly mandatory in the PSD specification and,
in some edge cases, may be missing or may not fully represent the document as it appears when opened in Photoshop.

![Photoshop file structure](docs/images/psd-file-structure.gif)

[Adobe Photoshop File Format Specification](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/PhotoshopFileFormats.htm#50577409_pgfId-1036097)

### Supported Color Modes

At the moment, Lyra supports decoding the following Photoshop color modes from Image Data:

- 8-bit RGB
- 8-bit CMYK
- 8-bit Indexed

Support for other modes (16-bit, 32-bit, Lab, Grayscale, Bitmap...) will be implemented.

### PSB Support

Lyra fully supports PSB (Photoshop Big Document Format) files.

- Successfully tested with ~3 GB PSB files
- Uses streaming / tiled decoding internally where possible to avoid loading entire images eagerly

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

### Future Direction

The PSD decoder is intentionally structured to allow future expansion.

---

## Keyboard Shortcuts & Controls

| Key            | Action                                                |
|----------------|-------------------------------------------------------|
| `←` `→`        | Previous / Next image                                 |
| `Home` `End`   | First / Last image                                    |
| `+` `-`        | Zoom in / Zoom out                                    |
| `Mouse Wheel`  | Zoom at cursor position                               |
| `0`            | Toggle **Fit to Screen** / **Original Size**          |
| `S`            | Toggle sampling mode                                  |
| `F`            | Toggle fullscreen                                     |
| `B`            | Toggle background mode                                |
| `I`            | Toggle image information overlay                      |
| `Return`       | Reveal image / directory path in native file explorer |
| `Esc`          | Exit application                                      |

### MacOS Specific

| Key           | Action                                  |
|---------------|-----------------------------------------|
| `⌘ ←` `⌘ →`   | First / Last image                      |
| `⌥ ←` `⌥ →`   | First / Last image within the directory |

### Open With / Drag & Drop

| Context                                    | How Lyra interprets it                   | Make a collection from files around | Recursion |
|--------------------------------------------|------------------------------------------|-------------------------------------|-----------|
| Single file                                | Anchor (Open / Open With / Double-click) | Yes                                 | No        |
| Multiple files (same directory)            | Selection                                | No                                  | No        |
| Single directory                           | Directory collection                     | No                                  | Yes       |
| Multiple directories                       | Multi-directory selection                | No                                  | Yes       |
| Mixed files from different directories     | Multi-directory selection                | No                                  | No        |

> Recursion applies only when directories are explicitly dropped.
> Opening or dropping files never implicitly expands into subdirectories.

> _Developer note:_ Lyra intentionally favors context-aware navigation.
> Opening a single image always implies “show me this image in relation to its neighbors”, not isolation.

---

## Prerequisites & Dependencies

Lyra Viewer is built on **.NET 9** and integrates several high-performance libraries designed to handle modern
image formats, accurate color processing, and GPU-accelerated rendering:

| Library              | Purpose                                                                | License       | Repository                                                        |
|----------------------|------------------------------------------------------------------------|---------------|-------------------------------------------------------------------|
| SDL3-CS              | Core graphics, input, and windowing                                    | zlib          | [github](https://github.com/ethereal-developers-club/SDL3-CS)     |
| SkiaSharp            | Hardware-accelerated 2D rendering                                      | BSD-3-Clause  | [github](https://github.com/mono/SkiaSharp)                       |
| Svg.Skia             | SVG parsing and rendering                                              | MIT           | [github](https://github.com/wieslawsoltes/Svg.Skia)               |
| SixLabors.ImageSharp | Support for TGA, TIFF, and legacy formats                              | Apache 2.0    | [github](https://github.com/SixLabors/ImageSharp)                 |
| LibHeifSharp         | HEIF / HEIC image decoding                                             | LGPL-3.0      | [github](https://github.com/0xC0000054/libheif-sharp)             |
| OpenEXR              | High-dynamic-range OpenEXR (.exr) decoding                             | BSD-3-Clause  | [github](https://github.com/AcademySoftwareFoundation/openexr)    |
| rgbe                 | Radiance HDR (.hdr) image decoding                                     | Public Domain | [webpage](https://www.graphics.cornell.edu/~bjw/rgbe.html)        |
| Unicolour            | Color space conversions & perceptual color math (used in PSD decoding) | MIT           | [github](https://github.com/waacton/Unicolour)                    |
| MetadataExtractor    | EXIF metadata extraction                                               | Apache 2.0    | [github](https://github.com/drewnoakes/metadata-extractor-dotnet) |

> _Native dependencies:_ Lyra does **not** bundle large native image libraries such as **libheif** or **OpenEXR**.
> These are expected to be provided by the system package manager (e.g. **Homebrew** on macOS,
> and other platform-specific package managers on Linux in the future).
>
> Lyra only ships **lightweight native interop wrappers** for HDR and EXR decoding.

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
