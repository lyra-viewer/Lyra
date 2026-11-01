using Lyra.ManagedCodecs.Scene;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Scene;

public sealed class IssueLogTests
{
    [Fact]
    public void IssuesPastTheCapAreCounted()
    {
        var log = new IssueLog(["first"]);
        for (var i = 0; i < 150; i++)
            log.Add($"issue {i}");

        var issues = log.ToList();

        Assert.Equal(101, issues.Count);
        Assert.Equal("first", issues[0]);
        Assert.Equal("…and 51 more.", issues[^1]);
    }
}
