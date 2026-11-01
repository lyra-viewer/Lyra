using System.Text;

namespace Lyra.ManagedCodecs.Scene;

internal sealed class IssueLog(IEnumerable<string>? initial = null)
{
    private const int Max = 100;

    private readonly List<string> _issues = [.. initial ?? []];
    private int _suppressed;

    public void Add(string text)
    {
        if (_issues.Count < Max)
            _issues.Add(text);
        else
            _suppressed++;
    }

    public IReadOnlyList<string> ToList() => _suppressed == 0 ? [.. _issues] : [.. _issues, $"…and {_suppressed} more."];
}

internal static class SceneText
{
    public static string? Words(string name)
    {
        var words = new List<string>();
        var word = new StringBuilder();

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var split = c is '_' or ' ' or '-';
            var camel = i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));

            if ((split || camel) && word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }

            if (!split)
                word.Append(c);
        }

        if (word.Length > 0)
            words.Add(word.ToString());

        return words.Count == 0 ? null : string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }
}