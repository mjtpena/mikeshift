using MikeShift.Core.Actions;

namespace MikeShift.Core.Tests.Infrastructure;

/// <summary>
/// An <see cref="IInteraction"/> with canned answers that records how often it was asked. Null answer = cancelled, except for the order
/// of a merge, which keeps the order it is given unless <see cref="Order"/> says otherwise.
/// </summary>
public sealed class FakeInteraction(CropChoice? crop = null, TimeRange? range = null) : IInteraction
{
    public int CropRequests { get; private set; }
    public int RangeCalls { get; private set; }
    public int OrderRequests { get; private set; }
    public List<string> Asked { get; } = [];

    /// <summary>The files the last order question was about, as it was asked.</summary>
    public IReadOnlyList<string>? OrderAsked { get; private set; }

    /// <summary>Answers the order question: the files in the order to use, or null to cancel. Default: as given.</summary>
    public Func<IReadOnlyList<string>, IReadOnlyList<string>?> Order { get; init; } = paths => paths;

    public Task<CropChoice?> ChooseCropAsync(string path, CancellationToken ct)
    {
        CropRequests++;
        Asked.Add(path);
        return Task.FromResult(crop);
    }

    public Task<TimeRange?> ChooseRangeAsync(string path, CancellationToken ct)
    {
        RangeCalls++;
        Asked.Add(path);
        return Task.FromResult(range);
    }

    public Task<IReadOnlyList<string>?> ChooseOrderAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        OrderRequests++;
        OrderAsked = paths;
        return Task.FromResult(Order(paths));
    }
}
