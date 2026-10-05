using Lyra.Imaging.Content;

namespace Lyra.Imaging.Decoding.Decoders.Animation;

/// <summary>
/// Damaged frames as a source reports them, passed on to the frame set once there is one; the
/// first frame is rendered before it exists.
/// </summary>
internal sealed class FrameDamageLog
{
    private readonly Dictionary<int, string> _damaged = new();
    private readonly Lock _gate = new();
    private VariantRasterContent? _frames;

    public void Report(int index, string why)
    {
        lock (_gate)
        {
            _damaged[index] = why;
            _frames?.RecordWarning(index, LoadWarning.PartiallyDecoded(why));
        }
    }

    public LoadWarning? WarningOf(int index)
    {
        lock (_gate)
            return _damaged.TryGetValue(index, out var why) ? LoadWarning.PartiallyDecoded(why) : null;
    }

    public void Attach(VariantRasterContent frames)
    {
        lock (_gate)
        {
            _frames = frames;

            foreach (var (index, why) in _damaged)
                frames.RecordWarning(index, LoadWarning.PartiallyDecoded(why));
        }
    }
}