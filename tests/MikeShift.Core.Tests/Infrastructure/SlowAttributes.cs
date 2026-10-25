namespace MikeShift.Core.Tests.Infrastructure;

/// <summary>
/// Gate for tests that take seconds each (real Media Foundation transcodes, Microsoft Word). They also carry
/// <c>[Trait("Category", "Slow")]</c>. By default they are reported as skipped, so a plain <c>dotnet test MikeShift.sln</c> stays quick;
/// set <c>MIKESHIFT_SLOW=1</c> to run them:
/// <code>$env:MIKESHIFT_SLOW = '1'; dotnet test MikeShift.sln</code>
/// </summary>
public static class Slow
{
    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("MIKESHIFT_SLOW") is "1" or "true";

    public const string Reason = "Slow test: set MIKESHIFT_SLOW=1 to run it.";
}

/// <summary>A <c>[Fact]</c> that runs only when <see cref="Slow.Enabled"/>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SlowFactAttribute : FactAttribute
{
    public SlowFactAttribute()
    {
        if (!Slow.Enabled) Skip = Slow.Reason;
    }
}

/// <summary>A <c>[Theory]</c> that runs only when <see cref="Slow.Enabled"/>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SlowTheoryAttribute : TheoryAttribute
{
    public SlowTheoryAttribute()
    {
        if (!Slow.Enabled) Skip = Slow.Reason;
    }
}
