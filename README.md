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

### Common Texture Formats (Essential)

| Format           | Description                                                                   |
|------------------|-------------------------------------------------------------------------------|
| `.png`           | Widely used lossless format, ideal for albedo, roughness, etc.                |
| `.jpg` / `.jpeg` | Lossy format used for color/albedo maps or previews.                          |
| `.jif` / `.jfif` | JPEG File Interchange Format — a JPEG variant seen in EXIF/exported previews. |
| `.tga`           | Game engine-friendly format supporting alpha channels.                        |
| `.tif` / `.tiff` | High-quality textures, often 16/32-bit, used in VFX.                          |
| `.bmp`           | Rare but used in older or internal pipelines.                                 |

### Modern / Web-Friendly Formats

| Format                      | Description                                                              |
|-----------------------------|--------------------------------------------------------------------------|
| `.webp`                     | Lightweight format with alpha support, used for previews and thumbnails. |
| `.heif` / `.heic` / `.avif` | High Efficiency format common on macOS/iOS systems.                      |

### High Dynamic Range Formats

| Format | Description                                                                |
|--------|----------------------------------------------------------------------------|
| `.exr` | OpenEXR format for HDR data (e.g. height, displacement, environment maps). |
| `.hdr` | Radiance HDR format, used in lighting environments and skies.              |

### Compressed / GPU-Friendly Formats

| Format               | Description                                                             |
|----------------------|-------------------------------------------------------------------------|
| ~~`.dds`~~           | ~~DirectDraw Surface, used in real-time engines (DXT/BC compression).~~ |
| ~~`.ktx` / `.ktx2`~~ | ~~Modern GPU-optimized format for real-time textures.~~                 |

### Vector / Specialized Formats

| Format          | Description                                        |
|-----------------|----------------------------------------------------|
| `.svg`          | Scalable vector graphics for masks or UI overlays. |
| `.psd` / `.psb` | Photoshop files (preview-only support).            |

### Minor Formats

| Format      | Description                                                |
|-------------|------------------------------------------------------------|
| `.ico`      | Icon files, often part of UI asset packs.                  |
| ~~`.icns`~~ | ~~macOS icon format, used in application packaging.~~      |
| ~~`.jp2`~~  | ~~JPEG 2000, rare but sometimes seen in asset pipelines.~~ |

---

## Keyboard Shortcuts & Controls

| Key            | Action                                                |
|----------------|-------------------------------------------------------|
| `←` / `→`      | Previous / Next image                                 |
| `Home` / `End` | First / Last image                                    |
| `+` / `-`      | Zoom in / Zoom out                                    |
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
| `⌘ ←` / `⌘ →` | First / Last image                      |
| `⌥ ←` / `⌥ →` | First / Last image within the directory |

### Drag & Drop

Open a file or directory by dragging it into Lyra Viewer.

---

## Prerequisites & Dependencies

Lyra Viewer is builton **.NET 9** and integrates several high-performance libraries designed to handle modern
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
brew install --cask lyra
```

### Linux

Not available yet.

---

## Future Ideas

- Channel preview (R, G, B, A, RGB)
- Histogram or levels display for HDR/EXR workflows
- Support for multi-layer EXR and PSD navigation
- Image export or format conversion
- Grid display

---

## TODO

- Explain concept of Lyra's collections in the README.md