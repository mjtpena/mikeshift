using System.Xml.Linq;
using MikeShift.Core.Shell;

namespace MikeShift.Core.Tests.Shell;

/// <summary>The package manifest and the code have to agree on the CLSID and the server's command line, or Explorer finds no menu entry (silently).</summary>
public class ManifestTests
{
    private static readonly XNamespace Com = "http://schemas.microsoft.com/appx/manifest/com/windows10";
    private static readonly XNamespace Desktop4 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";
    private static readonly XNamespace Desktop5 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/5";

    private static XDocument LoadManifest()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MikeShift.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return XDocument.Load(Path.Combine(dir!.FullName, "src", "MikeShift.App", "Package.appxmanifest"));
    }

    [Fact]
    public void The_command_class_is_served_by_the_app_exe_started_with_the_embedding_switch()
    {
        var servers = LoadManifest().Descendants(Com + "ExeServer").ToList();
        var server = Assert.Single(servers);
        Assert.Equal("MikeShift.exe", (string?)server.Attribute("Executable"));
        Assert.Equal(ShellIds.EmbeddingSwitch, (string?)server.Attribute("Arguments"));
        var cls = Assert.Single(server.Elements(Com + "Class"));
        Assert.Equal(ShellIds.CommandClsid, Guid.Parse((string)cls.Attribute("Id")!));
    }

    [Fact]
    public void Files_and_folders_both_get_the_one_verb_with_the_same_class()
    {
        var types = LoadManifest().Descendants(Desktop4 + "FileExplorerContextMenus").Single().Elements(Desktop5 + "ItemType").ToList();
        Assert.Equal(["*", "Directory"], types.Select(t => (string)t.Attribute("Type")!).Order(StringComparer.Ordinal).ToArray());
        foreach (var type in types)
        {
            var verb = Assert.Single(type.Elements(Desktop5 + "Verb"));
            Assert.Equal(ShellIds.CommandClsid, Guid.Parse((string)verb.Attribute("Clsid")!));
        }
        Assert.Equal(2, types.Select(t => (string)t.Element(Desktop5 + "Verb")!.Attribute("Id")!).Distinct().Count());
    }

    [Fact]
    public void The_new_namespaces_are_ignorable_so_an_older_Windows_does_not_reject_the_package()
    {
        var ignorable = ((string?)LoadManifest().Root!.Attribute("IgnorableNamespaces") ?? "").Split(' ');
        Assert.Contains("com", ignorable);
        Assert.Contains("desktop4", ignorable);
        Assert.Contains("desktop5", ignorable);
    }

    [Fact]
    public void The_pipe_name_is_per_user()
    {
        Assert.Equal("mikeshift-shell-S-1-5-21-1", ShellIds.PipeNameFor(" S-1-5-21-1 "));
        Assert.NotEqual(ShellIds.PipeNameFor("S-1-5-21-1"), ShellIds.PipeNameFor("S-1-5-21-2"));
        Assert.Throws<ArgumentException>(() => ShellIds.PipeNameFor(" "));
    }
}
