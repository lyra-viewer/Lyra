using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Loading;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding.Sweep;

/// <summary>
/// Every decoder, fed damaged copies of a valid file: cut short at many lengths, and with bytes
/// overwritten, many in the header. Each copy must still decode, or fail as the file's fault
/// (<see cref="LoadFailure.IsExpected"/>), within a bound and without leaving work running.
/// </summary>
public class DecoderSweepTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private const int Corruptions = 32;

    private readonly string _folder = Directory.CreateTempSubdirectory("lyra-sweep-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static TheoryData<string> Fixtures => new(SweepFixtures.All.Select(fixture => fixture.Name));

    /// <summary>What one decode came to: whether it showed anything, why not, and what was wrong with how it ended.</summary>
    private readonly record struct Outcome(bool Decoded, string? Problem, string? Failure = null);

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheIntactFile_Decodes(string name)
    {
        var fixture = Available(name);
        var outcome = Decode(fixture.Build(), fixture.Extension, $"{name}-intact");

        Assert.True(outcome is { Decoded: true, Problem: null }, outcome.Problem ?? outcome.Failure ?? "nothing was decoded");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryDamagedCopy_DecodesOrFailsAsTheFilesFault(string name)
    {
        var fixture = Available(name);
        var problems = new List<string>();

        foreach (var (label, damaged) in Damage(fixture.Build(), Seed(name)))
        {
            if (Decode(damaged, fixture.Extension, $"{name}-{label}").Problem is { } problem)
                problems.Add($"{label}: {problem}");
        }

        Assert.True(problems.Count == 0, $"{problems.Count} damaged copies of the {name} file ended badly:\n{string.Join("\n", problems)}");
    }

    private static SweepFixture Available(string name)
    {
        var fixture = SweepFixtures.Named(name);
        if (!fixture.IsAvailable)
            Assert.Skip($"What reads {name} is not available on this machine.");

        return fixture;
    }

    /// <summary>Cuts at lengths that land in headers and in data, then single and clustered overwrites.</summary>
    private static IEnumerable<(string Label, byte[] Bytes)> Damage(byte[] intact, int seed)
    {
        var n = intact.Length;
        int[] cuts = [0, 1, 2, 4, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, n / 4, n / 3, n / 2, 2 * n / 3, 3 * n / 4, n - 16, n - 4, n - 1];

        foreach (var cut in cuts.Where(cut => cut >= 0 && cut < n).Distinct().Order())
            yield return ($"cut-{cut}", intact[..cut]);

        var random = new Random(seed);

        for (var i = 0; i < Corruptions; i++)
        {
            var copy = intact.ToArray();
            var count = i < Corruptions / 2 ? 1 : 2 + random.Next(7);

            for (var c = 0; c < count; c++)
            {
                var at = random.Next(2) == 0 ? random.Next(Math.Min(n, 64)) : random.Next(n);
                copy[at] = (i % 3) switch
                {
                    0 => (byte)random.Next(256),
                    1 => 0xFF,
                    _ => 0x00
                };
            }

            yield return ($"overwrite-{i}", copy);
        }
    }

    /// <summary>Fixed per fixture, so a failure names a copy that can be made again.</summary>
    private static int Seed(string name) => name.Aggregate(17, (hash, c) => unchecked(hash * 31 + c));

    private Outcome Decode(byte[] bytes, string extension, string label)
    {
        var path = Path.Combine(_folder, label + extension);
        File.WriteAllBytes(path, bytes);

        var composite = new Composite(new FileInfo(path));
        using var cancel = new CancellationTokenSource();

        var decode = Task.Run(() => DecoderManager.GetDecoder(composite.ImageFormatType).DecodeAsync(composite, cancel.Token));

        if (!WaitQuietly(decode))
        {
            cancel.Cancel();
            return new Outcome(false, $"the decode did not return within {Patience.TotalSeconds:0} s");
        }

        if (!WaitQuietly(composite.BackgroundDecodeTask))
        {
            cancel.Cancel();
            return new Outcome(false, $"its background work did not finish within {Patience.TotalSeconds:0} s");
        }

        try
        {
            if (decode.Exception?.InnerException is { } thrown)
                return new Outcome(false, Judge(thrown), thrown.Message);

            return new Outcome(composite.Content is not null, null);
        }
        finally
        {
            composite.Dispose();
        }
    }

    private static bool WaitQuietly(Task task)
    {
        try
        {
            return task.Wait(Patience);
        }
        catch (AggregateException)
        {
            return true;
        }
    }

    /// <summary>Null when the failure is the file's; else what the failure says about the program.</summary>
    private static string? Judge(Exception thrown)
    {
        if (thrown is OperationCanceledException)
            return "it cancelled itself";

        if (LoadFailure.From(thrown).IsExpected)
            return null;

        var frame = thrown.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "no stack";
        return $"{thrown.GetType().Name}: {thrown.Message} ({frame})";
    }
}