using MikeShift.Core.Actions;

namespace MikeShift.Core.Tests.Infrastructure;

/// <summary>An <see cref="IInteraction"/> with canned answers that records how often it was asked. Null answer = cancelled.</summary>
public sealed class FakeInteraction(CropChoice? crop = null, TimeRange? range = null) : IInteraction
{
    public int CropRequests { get; private set; }
    public int RangeRequests { get; private set; }
    public List<string> Asked { get; } = [];

    public Task<CropChoice?> ChooseCropAsync(string path, CancellationToken ct)
    {
        CropRequests++;
        Asked.Add(path);
        return Task.FromResult(crop);
    }

    public Task<TimeRange?> ChooseRangeAsync(string path, CancellationToken ct)
    {
        RangeRequests++;
        Asked.Add(path);
        return Task.FromResult(range);
    }
}
