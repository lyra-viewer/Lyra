using Lyra.Common.Settings.Enums;
using Lyra.Common.SystemExtensions;

namespace Lyra.Renderer.Backends;

internal static class RendererSelection
{
    public static IReadOnlyList<Backend> ResolveCandidates(Backend configured, bool isMacOs)
    {
        var tryMetal = isMacOs && configured is Backend.Auto or Backend.Metal;
        Backend[] candidates = tryMetal ? [Backend.Metal, Backend.OpenGL] : [Backend.OpenGL];

        return candidates.Where(candidate => !candidate.HasAttribute<DisabledBackendAttribute>()).ToArray();
    }
    
    public static bool IsUnavailable(Backend configured, IReadOnlyList<Backend> candidates) 
        => configured != Backend.Auto && !candidates.Contains(configured);
    
    public static string? DescribeUnavailable(Backend configured, IReadOnlyList<Backend> candidates)
    {
        if (!IsUnavailable(configured, candidates))
            return null;

        return configured.HasAttribute<DisabledBackendAttribute>()
            ? $"The {configured.Alias()} backend is not implemented yet."
            : $"The {configured.Alias()} backend is not available on this platform.";
    }
}