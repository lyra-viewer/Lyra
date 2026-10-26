using Xunit;

namespace Lyra.Metadata.Tests.Support;

/// <summary>Expected output kept as text beside the tests.</summary>
internal static class Golden
{
    public static string Cell(string? value) =>
        value is null ? "(null)" : value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\0", "\\0");

    public static void Verify(string name, IEnumerable<string> actual)
    {
        var lines = actual.ToList();
        var path = Path.Combine(AppContext.BaseDirectory, "Golden", name + ".tsv");
        var expected = File.Exists(path) ? File.ReadAllLines(path) : [];

        var mismatch = Enumerable.Range(0, Math.Max(expected.Length, lines.Count))
            .FirstOrDefault(i => i >= expected.Length || i >= lines.Count || expected[i] != lines[i], -1);

        if (mismatch < 0)
            return;

        var produced = Path.Combine(AppContext.BaseDirectory, name + ".actual.tsv");
        File.WriteAllLines(produced, lines);

        var want = mismatch < expected.Length ? expected[mismatch] : "(nothing)";
        var got = mismatch < lines.Count ? lines[mismatch] : "(nothing)";
        Assert.Fail($"{name}, line {mismatch + 1}:\n  expected {want}\n  produced {got}\nAll produced lines: {produced}");
    }
}