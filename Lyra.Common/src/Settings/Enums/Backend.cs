using Lyra.Common.SystemExtensions;

namespace Lyra.Common.Settings.Enums;

public enum Backend
{
    [Alias("opengl")]
    OpenGL,
    
    [Alias("metal")]
    Metal
}