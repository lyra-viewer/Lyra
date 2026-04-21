using Lyra.UI.Components;

namespace Lyra.Renderer.GUI.Sections;

public interface IUISection
{
    IComponent Root { get; }
 
    void Refresh(UIState state);
}