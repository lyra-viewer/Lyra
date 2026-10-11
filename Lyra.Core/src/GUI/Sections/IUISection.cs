using Lyra.UI.Components;

namespace Lyra.GUI.Sections;

public interface IUISection
{
    IComponent Root { get; }
 
    void Refresh(UIState state);
}