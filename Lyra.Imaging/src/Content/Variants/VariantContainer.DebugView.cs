using System.Diagnostics;

namespace Lyra.Imaging.Content.Variants;

/// <summary>
/// The renditions as the debugger shows them: one row per index with its label and state, instead
/// of parallel lists and dictionaries keyed by index. Reads without the lock, as the debugger may
/// have stopped a thread holding it, so a row can be torn while another thread runs.
/// </summary>
[DebuggerDisplay("{Kind} {ActiveIndex}/{Variants.Count}, {ResidentCountForDebugger} resident")]
[DebuggerTypeProxy(typeof(DebugView))]
public sealed partial class VariantContainer
{
    private int ResidentCountForDebugger => _contents.Count(content => content is not null);

    private sealed class DebugView(VariantContainer set)
    {
        [DebuggerDisplay("{State,nq}", Name = "[{Index}] {Label,nq}")]
        public readonly record struct Row(int Index, string Label, string State);

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public Row[] Renditions =>
        [
            .. set.Variants.Select((variant, i) => new Row(i, variant.Label, StateOf(i)))
        ];

        public long ResidentBytes => set._contents.Sum(content => content?.ByteSize ?? 0);

        public long StandInBytes => set._standIns.Values.Sum(content => content.ByteSize);

        private string StateOf(int index)
        {
            var marks = new List<string>();

            if (index == set._shownIndex)
                marks.Add("shown");
            else if (index == set.ActiveIndex)
                marks.Add("active");

            if (index == set._pendingIndex)
                marks.Add("pending");

            if (index < set._contents.Count && set._contents[index] is { } content)
                marks.Add($"resident {content.ByteSize / 1024} KB");
            else if (set._standIns.TryGetValue(index, out var standIn))
                marks.Add($"stand-in {standIn.ByteSize / 1024} KB");

            if (set._failures.TryGetValue(index, out var failure))
                marks.Add($"failed: {failure.Message}");

            if (set._warnings.TryGetValue(index, out var warning))
                marks.Add($"warning: {warning.Message}");

            return marks.Count == 0 ? "-" : string.Join(", ", marks);
        }
    }
}
