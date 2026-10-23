using System.IO.Compression;

namespace MikeShift.Cli.Tests;

/// <summary>Zip end to end through the real executable (the only action W1 implements itself).</summary>
public class CliZipTests
{
    [Fact]
    public void Zip_single_file()
    {
        using var tmp = new TempDir();
        var file = tmp.WriteFile("hello.txt", "hello");
        using var guard = OriginalsGuard.Watch(file);

        var r = CliProcess.Run("--run", "Zip", file);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("", r.Stderr);
        var output = Assert.Single(r.StdoutLines);
        Assert.Equal(tmp.Combine("hello.zip"), output);
        using var z = ZipFile.OpenRead(output);
        Assert.Equal(["hello.txt"], z.Entries.Select(e => e.FullName).ToArray());
    }

    [Fact]
    public void Zip_folder_stores_it_under_its_own_name()
    {
        using var tmp = new TempDir();
        var folder = tmp.CreateDir("Docs");
        tmp.WriteFile("Docs/a.txt", "a");
        tmp.WriteFile("Docs/sub/b.txt", "b");
        using var guard = OriginalsGuard.Watch(folder);

        var r = CliProcess.Run("--run", "zip", folder);

        Assert.Equal(0, r.ExitCode);
        using var z = ZipFile.OpenRead(Assert.Single(r.StdoutLines));
        Assert.Equal(["Docs/", "Docs/a.txt", "Docs/sub/", "Docs/sub/b.txt"], z.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Zip_several_items_makes_Archive_zip_and_never_overwrites_it()
    {
        using var tmp = new TempDir();
        var a = tmp.WriteFile("a.txt", "a");
        var b = tmp.WriteFile("b.png", Fixtures.Png());

        var first = CliProcess.Run("--run", "Zip", a, b);
        var second = CliProcess.Run("--run", "Zip", a, b);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(tmp.Combine("Archive.zip"), Assert.Single(first.StdoutLines));
        Assert.Equal(tmp.Combine("Archive 2.zip"), Assert.Single(second.StdoutLines));
        using var z = ZipFile.OpenRead(tmp.Combine("Archive.zip"));
        Assert.Equal(["a.txt", "b.png"], z.Entries.Select(e => e.FullName).Order().ToArray());
    }

    [Fact]
    public void Zip_works_for_anything_including_unknown_types()
    {
        using var tmp = new TempDir();
        var odd = tmp.WriteFile("data.weird", Fixtures.Noise(2048));
        var r = CliProcess.Run("--run", "Zip", odd);
        Assert.Equal(0, r.ExitCode);
        Assert.True(File.Exists(Assert.Single(r.StdoutLines)));
    }

    [Fact]
    public void Zip_refuses_a_named_pipe_path()
    {
        var r = CliProcess.Run("--run", "Zip", @"\\.\pipe\mikeshift-cli-test");
        Assert.Equal(1, r.ExitCode);
        Assert.Equal("", r.Stdout);
    }
}
