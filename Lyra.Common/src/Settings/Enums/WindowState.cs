using Lyra.Common.SystemExtensions;

namespace Lyra.Common.Settings.Enums;

public enum WindowState
{
    [Alias("maximized")]
    Maximized,
    
    [Alias("normal")]
    Normal,
    
    [Alias("fullscreen")]
    Fullscreen
}