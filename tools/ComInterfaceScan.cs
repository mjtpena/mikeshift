// Build-time helper for the IL trimmer (Trimming.props): finds every [ComImport] interface in a list of assemblies.
//
//   dotnet run --file tools/ComInterfaceScan.cs -- --list <file with one assembly path per line> [--descriptor <out.xml>] [--linked <dir>]
//
//   --descriptor  writes an ILLink root descriptor that preserves each interface whole (preserve="all") whenever it is used at all
//                 (required="false"), so the trimmer cannot remove methods from it: a COM interface is called through its vtable in
//                 declaration order, and removing one method shifts every slot after it.
//   --linked      compares each of those interfaces in the trimmer's output (same file name in <dir>) with its input and exits 1, listing
//                 them, if any lost a method.
//
// Needs the .NET 10 SDK (a file-based app), nothing else.

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

string? Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

var list = Arg("--list") ?? throw new ArgumentException("--list is required");
var descriptor = Arg("--descriptor");
var linkedDir = Arg("--linked");

static string NameOf(MetadataReader m, TypeDefinition t)
{
    var outer = t.GetDeclaringType();
    var name = m.GetString(t.Name);
    if (!outer.IsNil) return NameOf(m, m.GetTypeDefinition(outer)) + "/" + name;
    var ns = m.GetString(t.Namespace);
    return ns.Length == 0 ? name : ns + "." + name;
}

// Every [ComImport] interface of an assembly: full (nested) name -> number of methods.
static Dictionary<string, int> ComInterfaces(string path, out string? assemblyName)
{
    var result = new Dictionary<string, int>();
    assemblyName = null;
    using var fs = File.OpenRead(path);
    using var pe = new PEReader(fs);
    if (!pe.HasMetadata) return result;
    var m = pe.GetMetadataReader();
    if (!m.IsAssembly) return result;
    assemblyName = m.GetString(m.GetAssemblyDefinition().Name);
    foreach (var h in m.TypeDefinitions)
    {
        var t = m.GetTypeDefinition(h);
        if ((t.Attributes & TypeAttributes.Import) != 0 && (t.Attributes & TypeAttributes.Interface) != 0)
            result[NameOf(m, t)] = t.GetMethods().Count;
    }
    return result;
}

var xml = new StringBuilder("<linker>" + Environment.NewLine);
var broken = new List<string>();
int total = 0;
foreach (var path in File.ReadAllLines(list).Where(l => l.Trim().Length > 0))
{
    Dictionary<string, int> before;
    string? asm;
    try { before = ComInterfaces(path.Trim(), out asm); }
    catch (BadImageFormatException) { continue; }   // a native file
    if (before.Count == 0 || asm is null) continue;
    total += before.Count;

    xml.AppendLine($"  <assembly fullname=\"{asm}\">");
    foreach (var name in before.Keys.OrderBy(n => n, StringComparer.Ordinal))
        xml.AppendLine($"    <type fullname=\"{name}\" preserve=\"all\" required=\"false\" />");
    xml.AppendLine("  </assembly>");

    if (linkedDir is not null)
    {
        var linked = Path.Combine(linkedDir, Path.GetFileName(path.Trim()));
        if (!File.Exists(linked)) continue;   // the whole assembly was removed
        foreach (var (name, count) in ComInterfaces(linked, out _))
            if (before.TryGetValue(name, out var was) && was != count)
                broken.Add($"{asm}: {name} has {count} of {was} methods");
    }
}

if (descriptor is not null)
{
    xml.AppendLine("</linker>");
    File.WriteAllText(descriptor, xml.ToString());
    Console.WriteLine($"{total} [ComImport] interfaces found; the ones that are used are preserved whole.");
}

if (broken.Count > 0)
{
    Console.Error.WriteLine("error MIKESHIFT0001: the IL trimmer removed methods from COM interfaces, which shifts their vtables and crashes the first call through them: " + string.Join("; ", broken));
    return 1;
}
if (linkedDir is not null) Console.WriteLine($"{total} [ComImport] interfaces checked: none lost a method.");
return 0;
