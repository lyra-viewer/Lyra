# Lyra - Technical Documentation

Implementation detail for [Lyra Viewer](../README.md): how images are decoded, color-managed, tone-mapped, and
how the Photoshop and GPU-texture models work.

---

## Contents

- [Technical Details](#technical-details)
- [Color Management](#color-management)
    - [What this means on a standard-gamut display](#what-this-means-on-a-standard-gamut-display)
    - [Rendering intent](#rendering-intent)
- [HDR / EDR](#hdr--edr)
    - [Tone mapping](#tone-mapping)
    - [EDR output](#edr-output)
    - [How much of an image stays as light](#how-much-of-an-image-stays-as-light)
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

---

## Technical Details

Lyra is built on .NET 9 with SDL3 for windowing and input, and SkiaSharp for hardware-accelerated rendering via OpenGL or Metal.
The architecture is designed around fast, non-blocking image loading:

- Decoded images are cached and adjacent files are preloaded in the background, so navigation feels instant even in large
  directories.
- Large PSD/PSB files use streaming and tiled decoding to avoid loading entire documents into memory - tested with files
  exceeding 3 GB.
- Large TIFFs work the same way: BigTIFF is read natively, and a gray or eight-bit color sheet whose raster exceeds 256
  MB is published as a streamed preview plus tiles decoded by region as the view asks for them. Sample layouts libtiff's
  RGBA interface refuses - 10, 12 and 14-bit samples, 32 and 64-bit, IEEE float, one-bit color - are read at their own
  depth instead, so a float TIFF reaches the HDR pipeline scene-referred rather than flattened to eight bits on the way in.

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

How these native libraries are shipped differs by platform - see [Native Libraries & Bundling](../README.md#native-libraries--bundling).

---

## Color Management

Lyra is color-managed from decode to screen. Wide-gamut images are never clamped to sRGB at decode time - the
conversion happens once, at draw time, against the gamut of the display being drawn to.

- **Decode** tags every image with the gamut it was authored in. That means the embedded ICC profile wherever one
  exists (PNG, JPEG, TIFF, JPEG 2000, HEIF / AVIF, PSD), the NCLX primaries when a HEIF / AVIF file carries those
  instead, and Display P3 for JPEG XL, which Lyra asks libjxl to decode into P3 rather than fold down to sRGB. An
  image carrying no color information at all is interpreted as sRGB - the only defensible assumption.
- **Display** tags the render surface with the gamut of the screen. On macOS that is Display P3. On Windows and
  Linux, Lyra asks the windowing system for the monitor's ICC profile and uses it; if the system publishes no
  profile, or publishes one that cannot be expressed as a matrix/transfer-function color space, the surface falls
  back to sRGB.
- **Draw** is where the transform happens, per frame, on the GPU. Because both ends carry real profiles, a
  Display-P3 photograph on a Display-P3 screen keeps its saturated colors instead of being flattened on the way in.

### What this means on a standard-gamut display

**Colors outside the display's physical gamut are folded into the ones it can reproduce.** This is correct behavior
rather than a defect, and it is documented here because it is easy to mistake for one.

The clearest illustration is the well-known **WebKit Display-P3 logo test image**, which circulates in PNG, JPEG XL
and other formats. It is constructed so the logo and its background are *different* colors in Display P3 but clamp to
the *same* color in sRGB. The intended outcome is:

| Display                 | Color-managed viewer                | Non-color-managed viewer |
|-------------------------|-------------------------------------|--------------------------|
| Display P3 / wide gamut | Logo visible                        | Logo visible             |
| sRGB / standard gamut   | **Flat rectangle - logo invisible** | Logo visible             |

So if Lyra renders a flat rectangle on a standard-gamut monitor, the pipeline is working exactly as intended.

### Rendering intent

Lyra converts using **relative colorimetric intent with clipping**, the same choice web browsers make. Colors inside
the display's gamut are reproduced exactly; colors outside it are clipped to the gamut boundary.

The alternative - **perceptual** intent - compresses the whole gamut inward so that out-of-gamut *relationships*
survive, at the cost of desaturating colors that were perfectly reproducible to begin with. That trades fidelity for
the preservation of differences, and Lyra does not make that trade: accuracy for the colors a display can show takes
precedence over a hint of the ones it cannot.

---

## HDR / EDR

HDR images are held as **scene-referred light** - linear half-float, the values the file actually carries, with
nothing clamped or curved at decode. The mapping to the display happens in a shader, per frame, at draw time. So
changing the curve or the exposure is a repaint.

This covers every high-dynamic-range source Lyra decodes:

| Source                 | Formats                                                                                                        |
|------------------------|----------------------------------------------------------------------------------------------------------------|
| Scene-referred raster  | OpenEXR `.exr`, Radiance HDR `.hdr`                                                                            |
| Floating-point JPEG XL | `.jxl` decoded to float                                                                                        |
| HDR textures           | BC6H (signed + unsigned), RGBA16F / RGBA32F, R16F / R32F, RGB16F, RG11B10, RGB9E5 - in `.dds`, `.ktx`, `.ktx2` |

### Tone mapping

| Curve                     | What it does                                                                                                           |
|---------------------------|------------------------------------------------------------------------------------------------------------------------|
| **ACES filmic** (default) | Highlights roll off smoothly instead of clipping harshly to white.                                                     |
| **Reinhard extended**     | The white point is measured from the image, so the top of the output range is spent on whatever is actually brightest. |
| **Clip**                  | No curve - clip at white and encode.                                                                                   |

The curve is applied to **luminance**, not per channel, and the channels are then scaled by that one factor.
Curving each channel separately compresses the largest hardest and drags bright color toward neutral; this does not.

**Exposure** applies a 2^n multiply before the curve, from -8 to +8 stops. This is the control that matters for
scene-referred content: an environment map's sun can sit thousands of times brighter than its sky, and pulling
exposure down moves both back onto the curve's slope, so the sun resolves into a disc instead of merging into the
sky.

### EDR output

On a display with headroom, Lyra draws highlights **above SDR white** rather than compressing them into it - the
sun in an EXR is rendered as brighter than the page's white.

> _Note:_ macOS only, for now. It needs an extended-range Metal surface (`RGBA16Float`, extended Display P3), which Lyra
> uses for the whole run on every display. macOS OpenGL has no extended-range path, so the `opengl` backend preference
> disables EDR. Windows (DXGI scRGB / HDR10) and Linux (Wayland color management) are not implemented yet - there, the
> SDR curve above is what you get.

### How much of an image stays as light

Half-float costs 8 bytes per pixel against 4 for a tone-mapped 8-bit image, so image size decides how much of it
can be kept as light:

| Image size                                                                                                                | Held as                                                                 | Result                                                                                |
|---------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------|---------------------------------------------------------------------------------------|
| Up to 32 MP                                                                                                               | One scene-referred half-float texture                                   | Live controls and EDR at every zoom level                                             |
| Above that, while it fits a quarter of the decoded-image cache (a 128 MP panorama is 1 GB, and does on a typical machine) | Scene-referred preview **plus** scene-referred tiles                    | Live controls and EDR at every zoom level                                             |
| Larger                                                                                                                    | Curve baked in at decode; only the display-sized preview stays as light | Controls and EDR apply at fit-to-window; zooming in steps down to the baked rendering |

When an image is too large to hold as light, the **HDR Decode** section says so in place of the controls rather
than disappearing. The cache budget is derived from installed RAM, so the middle row's ceiling is higher on a
larger machine.

> _Note:_ Thumbnails are always tone-mapped to 8-bit, deliberately - they feed perceptual hashing for the
> duplicates finder, which wants stable pixels.

---

## PSD / PSB Decoding Model

Lyra currently focuses on decoding the flattened **Image Data** section of Photoshop files, rather than individual
layers. This design choice prioritizes performance and fast previewing.

For PSD / PSB files, Lyra also surfaces the **layer hierarchy** in the sidebar - showing group structure, layer names,
and visibility state - independently of the flattened composite decode.

This is explicitly documented because the Image Data section is not strictly mandatory in the PSD specification and,
in some edge cases, may be missing or may not fully represent the document as it appears when opened in Photoshop.

![Photoshop file structure](images/psd-file-structure.gif)

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

![PSB Large](images/psd-large.png)

### ICC Color Profiles

See [Color Management](#color-management) for how profiles are handled across all formats; this section covers what is
specific to PSD / PSB.

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

<img src="images/psd-gui-example.png" width="400">

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

- **HDR formats** (BC6H, RGBA16F / RGBA32F, and the packed float formats) are scene-referred float and go through
  the same draw-time path as EXR and Radiance HDR - live curve, exposure, and EDR. See [HDR / EDR](#hdr--edr).
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

[Back to README](../README.md)
