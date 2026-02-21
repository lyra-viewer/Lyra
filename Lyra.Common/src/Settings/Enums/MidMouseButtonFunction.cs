using Lyra.Common.SystemExtensions;

namespace Lyra.Common.Settings.Enums;

public enum MidMouseButtonFunction
{
    [Alias("pan")]
    Pan,
    
    [Alias("exit")]
    Exit,
    
    [Alias("none")]
    None
}