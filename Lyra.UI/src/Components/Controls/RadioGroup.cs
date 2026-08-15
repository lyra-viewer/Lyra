using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;

namespace Lyra.UI.Components.Controls;

public sealed class RadioGroup<T> : VStack
    where T : notnull
{
    private readonly List<(T Value, RadioButton Button)> _options = [];

    private T _selected;

    /// <summary>Fired when the user picks a different option. Not raised by setting <see cref="Selected"/>.</summary>
    public event Action<T>? SelectionChanged;

    public T Selected
    {
        get => _selected;
        set => Apply(value, raise: false);
    }

    public RadioGroup(IEnumerable<T> values, Func<T, string> displayName, T initialSelection)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(displayName);

        _selected = initialSelection;
        HorizontalSize = SizeMode.Expand;
        VerticalSize = SizeMode.Shrink;

        foreach (var value in values)
        {
            var captured = value;

            var button = new RadioButton(displayName(value))
            {
                HorizontalSize = SizeMode.Expand,
                IsSelected = EqualityComparer<T>.Default.Equals(value, initialSelection)
            };

            button.Selected += () => Apply(captured, raise: true);

            _options.Add((value, button));
            AddComponent(button);
        }
    }

    private void Apply(T value, bool raise)
    {
        _selected = value;

        foreach (var (candidate, button) in _options)
            button.IsSelected = EqualityComparer<T>.Default.Equals(candidate, value);

        if (raise)
            SelectionChanged?.Invoke(value);
    }
}