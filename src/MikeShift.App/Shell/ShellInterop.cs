using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace MikeShift.App.Shell;

// COM and Win32 declarations of the Explorer extension (the out-of-process server that Explorer's right-click menu talks to).
//
// All COM here is source-generated ([GeneratedComInterface] / [GeneratedComClass], ComWrappers): nothing in this folder relies on the
// runtime's built-in COM marshalling, so it is trim-safe and keeps working without BuiltInComInteropSupport. Interface declarations list
// EVERY method in vtable order (unused ones with raw pointer parameters), because the order is the ABI.

internal static class Hr
{
    public const int Ok = 0;
    public const int False = 1;
    public const int NotImpl = unchecked((int)0x80004001);
    public const int NoInterface = unchecked((int)0x80004002);
    public const int Pointer = unchecked((int)0x80004003);
    public const int Fail = unchecked((int)0x80004005);
    public const int InvalidArg = unchecked((int)0x80070057);
    public const int NoAggregation = unchecked((int)0x80040110);
    public const int Unexpected = unchecked((int)0x8000FFFF);
}

/// <summary>EXPCMDSTATE.</summary>
internal static class CommandState
{
    public const int Enabled = 0;
    public const int Disabled = 1;
    public const int Hidden = 2;
}

/// <summary>EXPCMDFLAGS.</summary>
internal static class CommandFlags
{
    public const int Default = 0;
    public const int HasSubCommands = 0x1;
    public const int IsSeparator = 0x8;
}

/// <summary>IExplorerCommand, the interface Windows 11's modern menu (and Windows 10's menu for a packaged app) calls.</summary>
[GeneratedComInterface]
[Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
internal partial interface IExplorerCommand
{
    [PreserveSig] int GetTitle(IShellItemArray? items, out nint name);
    [PreserveSig] int GetIcon(IShellItemArray? items, out nint icon);
    [PreserveSig] int GetToolTip(IShellItemArray? items, out nint tip);
    [PreserveSig] int GetCanonicalName(out Guid name);
    [PreserveSig] int GetState(IShellItemArray? items, int okToBeSlow, out int state);
    [PreserveSig] int Invoke(IShellItemArray? items, nint bindContext);
    [PreserveSig] int GetFlags(out int flags);
    [PreserveSig] int EnumSubCommands(out IEnumExplorerCommand? commands);
}

[GeneratedComInterface]
[Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8")]
internal partial interface IEnumExplorerCommand
{
    /// <summary><paramref name="commands"/> points at an array of <paramref name="count"/> IExplorerCommand pointers the callee fills (each AddRef'd).</summary>
    [PreserveSig] int Next(uint count, nint commands, out uint fetched);
    [PreserveSig] int Skip(uint count);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IEnumExplorerCommand? clone);
}

[GeneratedComInterface]
[Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
internal partial interface IShellItemArray
{
    [PreserveSig] int BindToHandler(nint bindContext, in Guid handler, in Guid iid, out nint result);
    [PreserveSig] int GetPropertyStore(int flags, in Guid iid, out nint result);
    [PreserveSig] int GetPropertyDescriptionList(nint key, in Guid iid, out nint result);
    [PreserveSig] int GetAttributes(int flags, uint mask, out uint attributes);
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemAt(uint index, out IShellItem? item);
    [PreserveSig] int EnumItems(out nint result);
}

[GeneratedComInterface]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
internal partial interface IShellItem
{
    [PreserveSig] int BindToHandler(nint bindContext, in Guid handler, in Guid iid, out nint result);
    [PreserveSig] int GetParent(out nint parent);
    [PreserveSig] int GetDisplayName(uint form, out nint name);
    [PreserveSig] int GetAttributes(uint mask, out uint attributes);
    [PreserveSig] int Compare(nint other, uint hint, out int order);
}

[GeneratedComInterface]
[Guid("00000001-0000-0000-c000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig] int CreateInstance(nint outer, in Guid iid, out nint instance);
    [PreserveSig] int LockServer(int @lock);
}

[GeneratedComInterface]
[Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
internal partial interface IApplicationActivationManager
{
    [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string? arguments, int options, out uint processId);
    [PreserveSig] int ActivateForFile(nint a, nint b, nint c, out uint processId);
    [PreserveSig] int ActivateForProtocol(nint a, nint b, out uint processId);
}

internal static partial class Native
{
    public const uint CoinitMultithreaded = 0x0;
    public const uint ClsctxLocalServer = 0x4;
    public const uint ClsctxInprocServer = 0x1;
    public const uint RegclsMultipleUse = 1;

    [LibraryImport("ole32.dll")] public static partial int CoInitializeEx(nint reserved, uint flags);
    [LibraryImport("ole32.dll")] public static partial void CoUninitialize();
    [LibraryImport("ole32.dll")] public static partial int CoRegisterClassObject(in Guid clsid, nint unknown, uint context, uint flags, out uint cookie);
    [LibraryImport("ole32.dll")] public static partial int CoRevokeClassObject(uint cookie);
    [LibraryImport("ole32.dll")] public static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
    [LibraryImport("ole32.dll")] public static partial void CoTaskMemFree(nint memory);

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentPackageFamilyName")]
    public static unsafe partial int GetCurrentPackageFamilyName(ref uint length, char* name);

    /// <summary>Releases a string Explorer allocated for us (or that we allocated for Explorer) with CoTaskMemAlloc.</summary>
    public static string? TakeString(nint memory)
    {
        if (memory == 0) return null;
        try { return Marshal.PtrToStringUni(memory); }
        finally { CoTaskMemFree(memory); }
    }

    /// <summary>A CoTaskMem copy of <paramref name="text"/>, which is the owner-transfers-to-caller form IExplorerCommand wants for its out strings.</summary>
    public static nint ToCoTaskMem(string text) => Marshal.StringToCoTaskMemUni(text);
}
