using Lyra.Imaging.Content.Textures;
using Lyra.Renderer.Drawing;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

public class ContactSheetLayoutTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(12)]
    [InlineData(17)]
    public void TheLaidOutSheetIsTheSizeTheDecoderStated(int count)
    {
        using var content = new ContactSheetContent([.. Enumerable.Range(0, count).Select(i => new SheetCell($"{i}", null, "none"))]);
        using var sheet = SkiaCompositeContentDrawer.BuildSheet(content);

        var stated = ContactSheetContent.SizeFor(count);

        Assert.Equal(stated.Width, sheet.DesiredSize.Width);
        Assert.Equal(stated.Height, sheet.DesiredSize.Height);
    }
}