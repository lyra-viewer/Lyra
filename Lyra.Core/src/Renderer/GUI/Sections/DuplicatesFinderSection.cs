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
    private const string Regroup = "Regroup / Rescan";

    private readonly Collapsible _collapsible;
    private readonly CheckBox _exactCopiesOnly;
    private readonly ValueSlider _toleranceSlider;
    private readonly Button _findButton;
    private readonly Button _goBackButton;
    private readonly Label _noDuplicatesLabel;

    public Collapsible Collapsible => _collapsible;
    public IComponent Root => _collapsible;

    /// <summary>Raised when "Find Duplicates" / "Regroup" is clicked.</summary>
    public event Action? FindClicked;
    public event Action? GoBackClicked;
    public event Action? CloseClicked;
    public event Action<bool>? ExactCopiesOnlyChanged;
    public event Action<int>? ToleranceChanged;

    public DuplicatesFinderSection()
    {
        _exactCopiesOnly = new CheckBox("Exact copies only")
            .ExpandH()
            .PadTop(4f)
            .OnCheckedChanged(v => ExactCopiesOnlyChanged?.Invoke(v));

        _toleranceSlider = new ValueSlider(1, 9, 5)
            .ExpandH()
            .Padding(15f, 2f, 15f, 4f)
            .OnValueChanged(v => ToleranceChanged?.Invoke(v));

        _findButton = MenuButton(FindCaption)
            .OnClick(() => FindClicked?.Invoke());

        _goBackButton = MenuButton("Go Back")
            .Enabled(false)
            .OnClick(() => GoBackClicked?.Invoke());

        _noDuplicatesLabel = new Label("No duplicates found")
            .Color(Palette.Dim)
            .Transient()
            .Present(false)
            .PadTop(4f);

        _collapsible = new Collapsible("DUPLICATES FINDER")
            .ExpandH()
            .Expanded()
            .Present(false)
            .Children(
                _exactCopiesOnly,
                new Label("Perceptual tolerance:")
                    .Color(Palette.Muted)
                    .FontSize(11f)
                    .PadTop(8f),
                _toleranceSlider,
                _findButton,
                _goBackButton,
                MenuButton("Close", ButtonVariant.Danger)
                    .OnClick(() => CloseClicked?.Invoke()),
                _noDuplicatesLabel,
                new HStack()
                    .ExpandH()
                    .Transient()
                    .PadBottom(12f));
    }

    public void SetState(bool inDuplicatesMode, bool noDuplicatesFound)
    {
        _findButton.Text = inDuplicatesMode ? Regroup : FindCaption;
        _goBackButton.Enabled = inDuplicatesMode;
        _noDuplicatesLabel.Present = noDuplicatesFound && !inDuplicatesMode;
    }

    public void Show() => _collapsible.Present = true;
    public void Hide() => _collapsible.Present = false;

    private static Button MenuButton(string text, ButtonVariant variant = ButtonVariant.Default) =>
        new Button(text, variant)
            .CornerRadius(0f)
            .ExpandH()
            .PadTop(4f);

    public void Refresh(UIState state)
    {
        // State is pushed imperatively via SetState; nothing to pull from UIState.
    }
}
