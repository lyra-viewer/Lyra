using System.Reflection;
using Lyra.Imaging.Content;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

/// <summary>
/// Pins the contract the metadata panel relies on: rows come from the public properties of
/// ExifInfo, in declaration order, labeled by [Description], grouped by [EmptyLine], and opted
/// out of with [ExcludeFromSchema]. The schema is reflected once at type init, so a mistake here
/// would otherwise only show up as a missing row - hence the explicit coverage.
/// </summary>
public class ExifInfoSchemaTests
{
    [Fact]
    public void Rows_FollowDeclarationOrder()
    {
        var info = new ExifInfo { Software = "Lyra", Make = "Canon", Iso = "400" };

        var keys = info.ToKeyValuePairs().Where(e => !e.IsSeparator).Select(e => e.Key).ToList();

        Assert.Equal(["Make", "ISO", "Software"], keys);
    }

    [Fact]
    public void Rows_UseDescriptionAsLabelAndFallBackToTheFieldName()
    {
        var info = new ExifInfo { FNumber = "f/2.8", Model = "EOS R5" };

        var keys = info.ToKeyValuePairs().Select(e => e.Key).ToList();

        Assert.Contains("Aperture", keys);   // [Description("Aperture")] on FNumber
        Assert.Contains("Model", keys);      // no attribute, so the field name
    }

    [Fact]
    public void Rows_SkipEmptyValues()
    {
        var info = new ExifInfo { Make = "Canon", Model = "   " };

        var keys = info.ToKeyValuePairs().Select(e => e.Key).ToList();

        Assert.Equal(["Make"], keys);
    }

    [Fact]
    public void Rows_SeparateGroupsButNeverLeadTrailOrDouble()
    {
        // Make and Software sit in groups with two [EmptyLine] boundaries between them; the
        // empty groups in between must not produce stacked or dangling separators.
        var info = new ExifInfo { Make = "Canon", Software = "Lyra" };

        var entries = info.ToKeyValuePairs();

        Assert.Equal(3, entries.Count);
        Assert.False(entries[0].IsSeparator);
        Assert.True(entries[1].IsSeparator);
        Assert.False(entries[2].IsSeparator);
    }

    [Fact]
    public void Rows_ExcludeOptedOutProperties()
    {
        // These carry values for decoders and callers, not rows, and say so with the attribute.
        var info = new ExifInfo
        {
            OrientationValue = ExifOrientation.Rotate90Cw,
            ContainerRotation = ExifOrientation.Rotate180,
            Status = ExifStatus.Failed
        };

        var entries = info.ToKeyValuePairs();

        Assert.Empty(entries);
        Assert.False(info.HasData());
    }

    [Fact]
    public void EveryPropertyIsEitherARowOrExplicitlyExcluded()
    {
        // The schema builder throws on a violation, which surfaces as a TypeInitializationException
        // from whichever test touches ExifInfo first. This states the rule directly instead, so the
        // failure names the property and the fix.
        var offenders = typeof(ExifInfo)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType != typeof(string))
            .Where(property => !property.IsDefined(typeof(ExcludeFromSchemaAttribute), false))
            .Select(property => property.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Not a string and not marked [ExcludeFromSchema]: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Rows_AreBuiltOnceAndReused()
    {
        var info = new ExifInfo { Make = "Canon" };

        Assert.Same(info.ToKeyValuePairs(), info.ToKeyValuePairs());
    }

    [Fact]
    public void Status_DistinguishesAnEmptyRecordFromAFailedOne()
    {
        var empty = new ExifInfo();
        var failed = ExifInfo.Failed();

        Assert.False(empty.HasData());
        Assert.True(empty.IsValid());

        Assert.False(failed.HasData());
        Assert.False(failed.IsValid());
        Assert.Equal(ExifStatus.Failed, failed.Status);
    }

    [Fact]
    public void Failed_ReturnsAFreshRecordRatherThanASharedSentinel()
    {
        // The old design handed every failed parse the same mutable static instance.
        Assert.NotSame(ExifInfo.Failed(), ExifInfo.Failed());
    }

    [Fact]
    public void Seal_BuildsTheRowsBeforePublication()
    {
        var info = new ExifInfo { Make = "Canon" };

        var sealedInfo = info.Seal();

        Assert.Same(info, sealedInfo);
        Assert.Same(info.ToKeyValuePairs(), sealedInfo.ToKeyValuePairs());
        Assert.Contains(new ExifEntry("Make", "Canon"), sealedInfo.ToKeyValuePairs());
    }

    [Fact]
    public void ToLines_MirrorsRowsWithBlanksForSeparators()
    {
        var info = new ExifInfo { Make = "Canon", Software = "Lyra" };

        Assert.Equal(["Make: Canon", "", "Software: Lyra"], info.ToLines());
    }
}
