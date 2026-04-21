namespace Lyra.Renderer.GUI.Support;

public sealed class KeyColumnRegistry
{
    private readonly Dictionary<string, float> _widths = new();

    /// <summary>
    /// Reset all columns. Called once per refresh cycle by UIManager.
    /// </summary>
    public void BeginFrame() => _widths.Clear();

    public void Report(string columnId, float width)
    {
        var current = _widths.GetValueOrDefault(columnId);
        if (width > current)
            _widths[columnId] = width;
    }

    public float Get(string columnId) => _widths.GetValueOrDefault(columnId);
}