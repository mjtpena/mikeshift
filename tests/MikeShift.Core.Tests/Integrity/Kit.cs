using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MikeShift.Core.Actions;
using Xunit.Sdk;

namespace MikeShift.Core.Tests.Integrity;

/// <summary>
/// The integrity suite's driver. Every conversion goes through the real action catalog (the same <c>ActionCatalog.For</c> +
/// <c>ActionDef.Run</c> the wheel and the CLI use), then the output is judged by decoders that are NOT the code that wrote it.
/// <para>Every run also proves the two "always" rules: the originals are byte-identical afterwards (SHA-256), and no
/// <c>.mikeshift-tmp</c> staging folder is left next to the inputs or in the (fake) Downloads folder.</para>
/// </summary>
internal static class Kit
{
    private static readonly AsyncLocal<Capabilities?> s_caps = new();

    /// <summary>What this PC can do, or the override installed by <see cref="WithCaps"/> (e.g. "pretend Word is not installed").</summary>
    public static Capabilities Caps => s_caps.Value ?? Capabilities.Current;

    /// <summary>Runs the following conversions of the current test with different capabilities until disposed.</summary>
    public static IDisposable WithCaps(Capabilities caps)
    {
        s_caps.Value = caps;
        return new Restore();
    }

    private sealed class Restore : IDisposable
    {
        public void Dispose() => s_caps.Value = null;
    }

    /// <summary>True if the catalog offers <paramref name="title"/> for <paramref name="inputs"/> on this PC.</summary>
    public static bool Offered(string title, params string[] inputs) =>
        ActionCatalog.Find(ActionCatalog.For(inputs, Caps), title) is not null;

    /// <summary>Titles the catalog offers for <paramref name="inputs"/>, as a readable list.</summary>
    public static string OfferedList(IReadOnlyList<string> inputs) =>
        string.Join(", ", ActionCatalog.For(inputs, Caps).Select(a => a.Title));

    /// <summary>
    /// Runs the catalog action <paramref name="title"/> over <paramref name="inputs"/> and returns the paths it produced.
    /// Fails the test if the action is not offered, reports any failure, or touches an original / leaves staging files.
    /// </summary>
    public static async Task<IReadOnlyList<string>> RunAsync(string title, IInteraction? interaction, params string[] inputs)
    {
        var result = await TryRunAsync(title, interaction, inputs);
        if (result.Failures.Count > 0 || result.Cancelled)
            throw new XunitException($"'{title}' on {Names(inputs)} failed: "
                                     + (result.Cancelled ? "cancelled" : string.Join(" | ", result.Failures.Select(f => f.Message))));
        return result.Outputs.Select(o => o.Path).ToList();
    }

    public static Task<IReadOnlyList<string>> RunAsync(string title, params string[] inputs) => RunAsync(title, null, inputs);

    /// <summary>Like <see cref="RunAsync(string, IInteraction?, string[])"/> but returns the whole result (for legitimate refusals).</summary>
    public static async Task<ActionResult> TryRunAsync(string title, IInteraction? interaction, params string[] inputs)
    {
        var caps = Caps;
        var action = ActionCatalog.Find(ActionCatalog.For(inputs, caps), title)
                     ?? throw new XunitException($"'{title}' is not offered for {Names(inputs)}. Offered: {OfferedList(inputs)}");

        using var guard = OriginalsGuard.Watch(inputs);
        var result = await action.Run(inputs, new ActionContext(caps, interaction), CancellationToken.None);
        guard.AssertUnchanged();
        AssertNoStagingLeftovers(inputs);
        return result;
    }

    private static string Names(IEnumerable<string> inputs) => string.Join(", ", inputs.Select(Path.GetFileName));

    /// <summary>No hidden <c>*.mikeshift-tmp</c> folder or file in any input's folder (nor the fake Downloads folder).</summary>
    public static void AssertNoStagingLeftovers(IEnumerable<string> inputs)
    {
        var dirs = inputs.Select(i => Directory.Exists(i) ? Path.GetDirectoryName(Path.GetFullPath(i).TrimEnd('\\', '/'))! : Path.GetDirectoryName(Path.GetFullPath(i))!)
            .Append(Naming.DownloadsOverride ?? "")
            .Where(d => d.Length > 0 && Directory.Exists(d))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dirs)
        {
            var left = Directory.EnumerateFileSystemEntries(d, "*.mikeshift-tmp", SearchOption.AllDirectories).FirstOrDefault();
            if (left is not null) throw new XunitException("A staging folder was left behind: " + left);
        }
    }

    public static string Sha256(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    /// <summary>The single output of an action that must produce exactly one thing.</summary>
    public static string One(IReadOnlyList<string> outputs)
    {
        Assert.Single(outputs);
        return outputs[0];
    }

    /// <summary>Collapses all whitespace runs to one space and trims. Used to compare text from different extractors.</summary>
    public static string NormalizeText(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // ---------------------------------------------------------------- matrix recording

    private static readonly object s_lock = new();

    /// <summary>
    /// Appends one row to the matrix file (tab separated: category, input, action, validator, result, metric). The file is
    /// <c>$MIKESHIFT_INTEGRITY_MATRIX</c> when set; otherwise nothing is written. The report generator reads it.
    /// </summary>
    public static void Record(string category, string input, string action, string validator, string metric)
    {
        var file = Environment.GetEnvironmentVariable("MIKESHIFT_INTEGRITY_MATRIX");
        if (string.IsNullOrEmpty(file)) return;
        var line = string.Join('\t', category, input, action, validator, "pass", metric.Replace('\t', ' ').Replace('\n', ' ')) + "\n";
        lock (s_lock)
        {
            try { File.AppendAllText(file, line, new UTF8Encoding(false)); }
            catch (IOException) { /* the report is a courtesy; a locked file must not fail a conversion test */ }
        }
    }

    public static string F(double v, string fmt = "0.0") => v.ToString(fmt, CultureInfo.InvariantCulture);

    public static string Db(double psnr) => double.IsPositiveInfinity(psnr) ? "exact" : F(psnr) + " dB";
}
