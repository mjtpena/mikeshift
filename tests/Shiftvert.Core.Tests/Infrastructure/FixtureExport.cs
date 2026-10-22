namespace Shiftvert.Core.Tests.Infrastructure;

/// <summary>
/// Not a real test: <c>scripts/make-fixtures.ps1</c> sets <c>SHIFTVERT_FIXTURES_OUT</c> and runs this to write every
/// package's fixtures into that folder for manual testing. Without the variable it does nothing.
/// </summary>
public class FixtureExport
{
    [Fact]
    public void Export_when_requested()
    {
        var dir = Environment.GetEnvironmentVariable("SHIFTVERT_FIXTURES_OUT");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Fixtures.WriteAll(dir);
    }
}
