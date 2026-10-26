using Lyra.Metadata.Icc;

namespace Lyra.Imaging.Metadata;

internal readonly record struct IccFacts(string ColorSpace, string ProfileName)
{
    public static IccFacts From(IccProfile? profile) => profile is null ? default : new IccFacts(profile.ColorSpace, profile.Description ?? string.Empty);
}