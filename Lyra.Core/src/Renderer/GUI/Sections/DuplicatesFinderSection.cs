using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Controls.Button;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;

namespace Lyra.Renderer.GUI.Sections;

/// <summary>
/// Sidebar collapsible that drives the duplicate scan. Hidden until revealed by the menu;
/// state is pushed imperatively via <see cref="SetState"/> and the buttons surface as events.
/// </summary>
public sealed class DuplicatesFinderSection : IUISection
{
    private const string FindCaption = "Find Duplicates";
    private const string NewSearchCaption = "New Search";

    private readonly Collapsible _collapsible;
    private readonly Button _findButton;
    private readonly Button _goBackButton;
    private readonly Label _noDuplicatesLabel;

    public Collapsible Collapsible => _collapsible;
    public IComponent Root => _collapsible;

    public event Action? FindClicked;
    public event Action? GoBackClicked;
    public event Action? CloseClicked;

    public DuplicatesFinderSection()
    {
        _findButton = new Button(FindCaption)
        {
            CornerRadius = 0f,
            HorizontalSize = SizeMode.Expand,
            Padding = new Padding(0, 4f, 0, 0)
        };
        _findButton.Click += () => FindClicked?.Invoke();

        _goBackButton = new Button("Go Back")
        {
            CornerRadius = 0f,
            HorizontalSize = SizeMode.Expand,
            Enabled = false,
            Padding = new Padding(0, 4f, 0, 0)
        };
        _goBackButton.Click += () => GoBackClicked?.Invoke();

        var closeButton = new Button("Close", ButtonVariant.Danger)
        {
            CornerRadius = 0f,
            HorizontalSize = SizeMode.Expand,
            Padding = new Padding(0, 4f, 0, 0)
        };
        closeButton.Click += () => CloseClicked?.Invoke();

        _noDuplicatesLabel = new Label("No duplicates found")
        {
            Color = Palette.Dim,
            Transient = true,
            Present = false,
            Padding = new Padding(0, 4f, 0, 0)
        };
        
        var bottomSeparator = new HStack
        {
            HorizontalSize = SizeMode.Expand,
            Transient = true,
            Padding = new Padding(0, 0, 0, 12f)
        };

        _collapsible = new Collapsible("DUPLICATES FINDER")
        {
            HorizontalSize = SizeMode.Expand,
            IsExpanded =  true,
            Present = false // revealed by the menu
        };
        _collapsible.AddComponents(_findButton, _goBackButton, closeButton, _noDuplicatesLabel, bottomSeparator);
    }

    public void SetState(bool inDuplicatesMode, bool noDuplicatesFound)
    {
        _findButton.Text = inDuplicatesMode ? NewSearchCaption : FindCaption;
        _goBackButton.Enabled = inDuplicatesMode;
        _noDuplicatesLabel.Present = noDuplicatesFound && !inDuplicatesMode;
    }

    public void Show() => _collapsible.Present = true;
    public void Hide() => _collapsible.Present = false;

    public void Refresh(UIState state)
    {
        // State is pushed imperatively via SetState; nothing to pull from UIState.
    }
}
