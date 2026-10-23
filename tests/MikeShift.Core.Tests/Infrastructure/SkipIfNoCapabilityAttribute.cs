using System.Reflection;
using MikeShift.Core;

namespace MikeShift.Core.Tests.Infrastructure;

/// <summary>
/// A <c>[Fact]</c> that is skipped (reported as skipped, not deleted) when this machine lacks a capability, as decided by
/// <see cref="Capabilities.Current"/>:
/// <code>[SkipIfNoCapability(nameof(Capabilities.HeifEncode))] public async Task Heic_roundtrip() { ... }</code>
/// Several names may be given; all must be present.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SkipIfNoCapabilityAttribute : FactAttribute
{
    public SkipIfNoCapabilityAttribute(params string[] capabilities)
    {
        Skip = CapabilityCheck.SkipReason(capabilities);
    }
}

/// <summary>The <c>[Theory]</c> counterpart of <see cref="SkipIfNoCapabilityAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SkipIfNoCapabilityTheoryAttribute : TheoryAttribute
{
    public SkipIfNoCapabilityTheoryAttribute(params string[] capabilities)
    {
        Skip = CapabilityCheck.SkipReason(capabilities);
    }
}

internal static class CapabilityCheck
{
    /// <summary>Null if every named capability is available, otherwise a skip reason.</summary>
    public static string? SkipReason(IEnumerable<string> names)
    {
        var caps = Capabilities.Current;
        var missing = new List<string>();
        foreach (var name in names)
        {
            var prop = typeof(Capabilities).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                       ?? throw new ArgumentException($"Unknown capability '{name}'.");
            if (prop.GetValue(caps) is not true) missing.Add(name);
        }
        return missing.Count == 0 ? null : "Missing capability: " + string.Join(", ", missing);
    }
}
