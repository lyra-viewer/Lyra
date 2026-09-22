using Lyra.Common;
using Xunit;

namespace Lyra.Core.Tests.Startup;

/// <summary>
/// A run's log has to survive the next start. The log is what the error dialog points at, and the
/// first thing anyone does after a failure is start the application again - which used to empty
/// the file before it had been read.
/// </summary>
public class LogRotationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lyra-log-{Guid.NewGuid():N}");

    private string Current => Path.Combine(_directory, "log.txt");
    private string Previous => Path.Combine(_directory, "log.previous.txt");

    public LogRotationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void TheRunBeforeThisOneIsKept()
    {
        File.WriteAllText(Current, "what happened last time");

        Assert.True(Logger.TryKeepAsPrevious(Current, Previous));

        Assert.False(File.Exists(Current)); // a fresh log starts here
        Assert.Equal("what happened last time", File.ReadAllText(Previous));
    }

    [Fact]
    public void OnlyOneRunBackIsKept()
    {
        File.WriteAllText(Previous, "two runs ago");
        File.WriteAllText(Current, "last run");

        Assert.True(Logger.TryKeepAsPrevious(Current, Previous));
        Assert.Equal("last run", File.ReadAllText(Previous));
    }

    [Fact]
    public void AFirstEverRunHasNothingToKeep()
    {
        Assert.True(Logger.TryKeepAsPrevious(Current, Previous));

        Assert.False(File.Exists(Current));
        Assert.False(File.Exists(Previous));
    }

    [Fact]
    public void AnUnmovableLogIsReported_SoTheCallerCanEmptyItInstead()
    {
        File.WriteAllText(Current, "this run");
        
        Directory.CreateDirectory(Previous);

        Assert.False(Logger.TryKeepAsPrevious(Current, Previous));
        Assert.True(File.Exists(Current));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
