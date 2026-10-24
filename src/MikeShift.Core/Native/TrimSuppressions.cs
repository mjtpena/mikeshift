using System.Diagnostics.CodeAnalysis;

// IL2050 ("correctness of COM interop cannot be guaranteed after trimming. Interfaces and interface members might be removed") is the
// trimmer's advisory for any method that calls a P/Invoke with a COM interface in its signature. ILLink reports it at the CALLER, so the
// suppression sits on MfInterop (the one place that calls Media Foundation) and on the CsWin32 P/Invoke class below. What the warning
// is about is taken care of:
//   * the interfaces (IMFSourceReader, IMFAttributes, ...) are [ComImport] types generated into this assembly, and this assembly is a
//     trim root in the shipped apps (TrimmerRootAssembly in MikeShift.App.csproj): none of its types or members is removed, so the
//     vtable layouts the runtime marshals against are intact;
//   * the runtime's built-in COM interop is switched on (BuiltInComInteropSupport in Trimming.props), which trimmed apps otherwise lose.
// Both are checked by running the trimmed package (see docs/developer/testing.md).
namespace MikeShift.Core
{
    internal static class TrimJustification
    {
        public const string ComInterop = "The COM interfaces are [ComImport] types in the trim-root assemblies MikeShift.Core and MikeShift and built-in COM interop is enabled; see Native\\TrimSuppressions.cs.";
    }
}

namespace Windows.Win32
{
    [UnconditionalSuppressMessage("Trimming", "IL2050", Justification = MikeShift.Core.TrimJustification.ComInterop)]
    internal static partial class PInvoke;
}
