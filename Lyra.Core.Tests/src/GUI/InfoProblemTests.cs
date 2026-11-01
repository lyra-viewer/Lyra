using Lyra.GUI.Sections;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Xunit;

namespace Lyra.Core.Tests.GUI;

public class InfoProblemTests
{
    private static Composite Failed(LoadFailureKind kind, string detail) => new(new FileInfo("model.gltf"))
    {
        State = CompositeState.Failed,
        Failure = new LoadFailure(kind, detail)
    };

    [Fact]
    public void NothingToShowIsANote()
    {
        var problem = InfoSection.ProblemOf(Failed(LoadFailureKind.NothingToShow, "The model holds no textures"));

        Assert.Equal(new InfoSection.Problem(InfoSection.ProblemLevel.Note, "Nothing to show", "The model holds no textures"), problem);
    }

    [Fact]
    public void AFailedDecodeIsAnError() =>
        Assert.Equal(InfoSection.ProblemLevel.Error, InfoSection.ProblemOf(Failed(LoadFailureKind.DecodeFailed, "bad"))?.Level);
}
