using Lyra.Common.SystemExtensions;

namespace Lyra.Common.Settings.Enums;

public enum Backend
{
    [Alias("opengl")]
    OpenGL,
    
    [Alias("metal")]
    Metal,

    [Alias("auto")]
    Auto,

    [DisabledBackend]
    [Alias("vulkan")]
    Vulkan,
    
    [DisabledBackend]
    [Alias("dx")]
    DirectX
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class DisabledBackendAttribute : Attribute;