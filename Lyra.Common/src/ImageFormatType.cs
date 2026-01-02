using System.ComponentModel;

namespace Lyra.Common;

// TODO register codecs from here, create new attribute
public enum ImageFormatType
{
    [FileExtension([".bmp"])]
    [Description("Windows Bitmap")]
    Bmp,

    [DisabledType]
    [FileExtension([".dds"])]
    [Description("DirectDraw Surface Texture")]
    Dds, // TODO

    [FileExtension([".exr"])]
    [Description("OpenEXR Image")]
    Exr,

    [FileExtension([".hdr"])]
    [Description("Radiance HDR Image")]
    Hdr,

    [FileExtension([".heic", ".heif", ".avif"])]
    [Description("HEIF Image")]
    Heif,

    [FileExtension([".ico"])]
    [Description("Windows Icon")]
    Ico,
    
    [DisabledType]
    [FileExtension([".icns"])]
    [Description("macOS Icon")]
    Icns, // TODO

    [FileExtension([".jif", ".jfif"])]
    [Description("JPEG File Interchange Format")]
    Jfif,

    [DisabledType]
    [FileExtension([".jp2"])]
    [Description("JPEG 2000 Image")]
    Jp2, // TODO

    [FileExtension([".jpg", ".jpeg"])]
    [Description("JPEG Image")]
    Jpeg,

    [DisabledType]
    [FileExtension([".ktx", ".ktx2"])]
    [Description("Khronos Texture")]
    Ktx, // TODO

    [FileExtension([".png"])]
    [Description("PNG Image")]
    Png,
    
    [FileExtension([".psd", ".psb"])]
    [Description("Adobe Photoshop Document")]
    Psd,

    [FileExtension([".svg"])]
    [Description("Scalable Vector Graphics")]
    Svg,

    [FileExtension([".tga"])]
    [Description("TARGA Image")]
    Tga,

    [FileExtension([".tif", ".tiff"])]
    [Description("Tagged Image File Format")]
    Tiff,

    [FileExtension([".webp"])]
    [Description("WebP Image")]
    Webp,

    [Description("Unknown Format")]
    Unknown
}

[AttributeUsage(AttributeTargets.Field)]
public class FileExtensionAttribute(string[] extensions) : Attribute
{
    public string[] Extensions => extensions;
}

[AttributeUsage(AttributeTargets.Field)]
public class DisabledTypeAttribute : Attribute;

[AttributeUsage(AttributeTargets.Field)]
public class DisabledPreloadAttribute : Attribute;