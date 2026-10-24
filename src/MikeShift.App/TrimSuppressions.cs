using System.Diagnostics.CodeAnalysis;

// IL2050 ("correctness of COM interop cannot be guaranteed after trimming. Interfaces and interface members might be removed") is the
// trimmer's advisory for any method that calls a P/Invoke with a COM interface in its signature. ILLink reports it at the CALLER, so the
// suppression sits on the few classes and methods that make such calls (WheelAutomation, WheelDropTarget.Register, FilePicker.ItemFor,
// DataObjectInspector.ReadShellItems), and on the CsWin32 P/Invoke class below. What the warning is about is taken care of:
//   * the interfaces are [ComImport] types in this assembly or in MikeShift.Core, and both are trim roots in the shipped apps
//     (TrimmerRootAssembly in MikeShift.App.csproj): none of their types or members is removed, so the vtable layouts the runtime
//     marshals against are intact;
//   * the runtime's built-in COM interop is switched on (BuiltInComInteropSupport in Trimming.props), which trimmed apps otherwise lose.
// Both are verified by the trimmed-package test runs in docs/test-runs/2026-10-24-package-size.md.
namespace MikeShift.App
{
    internal static class TrimJustification
    {
        public const string ComInterop = "The COM interfaces are [ComImport] types in the trim-root assemblies MikeShift and MikeShift.Core and built-in COM interop is enabled; see TrimSuppressions.cs.";
    }
}

namespace Windows.Win32
{
    [UnconditionalSuppressMessage("Trimming", "IL2050", Justification = MikeShift.App.TrimJustification.ComInterop)]
    internal static partial class PInvoke;
}
