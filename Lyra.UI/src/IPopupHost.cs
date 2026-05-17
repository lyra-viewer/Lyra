using Lyra.UI.Components;
using SkiaSharp;

namespace Lyra.UI;

public interface IPopupHost
{
    void ShowPopup(IComponent content, SKPoint position, Action? onDismiss = null);
    
    void DismissPopup();
}