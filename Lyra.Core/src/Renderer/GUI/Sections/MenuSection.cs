using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Controls.Button;
using Lyra.UI.SupportingTypes;

namespace Lyra.Renderer.GUI.Sections;

public sealed class MenuSection : IUISection
{
    public Collapsible Collapsible { get; }

    public IComponent Root => Collapsible;

    /// <summary>
    /// Fired when any of the menu's buttons is clicked.
    /// Parameter is the button's label text.
    /// </summary>
    public event Action<string>? ButtonClicked;

    public MenuSection()
    {
        string[] labels =
        [
            "DEFAULT BUTTON", "PRIMARY BUTTON", "OUTLINE BUTTON",
            "GHOST BUTTON", "DANGER BUTTON", "LINK BUTTON"
        ];

        var buttons = labels.Select(label =>
        {
            var btn = new Button(label, ButtonVariant.Default)
            {
                CornerRadius = 0f,
                HorizontalSize = SizeMode.Expand
            };
            btn.Click += () => ButtonClicked?.Invoke(btn.Text);
            return btn;
        }).ToArray();

        var collapsible = new Collapsible("MENU")
        {
            HorizontalSize = SizeMode.Expand
        };
        collapsible.AddComponents(buttons);

        Collapsible = collapsible;
    }

    public void Refresh(UIState state)
    {
        // Menu is static; nothing to refresh.
    }
}