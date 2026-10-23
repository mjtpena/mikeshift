<#
.SYNOPSIS
  Starts MikeShift.exe with a dev flag, captures one of ITS OWN windows to a PNG with PrintWindow, and stops it.

.DESCRIPTION
  No input is sent to any other program: the script only reads our own window's pixels (PrintWindow, PW_RENDERFULLCONTENT) and
  can drive our own controls through UI Automation patterns (-Invoke "Button name"). The process is started with
  MIKESHIFT_NO_SINGLE_INSTANCE=1 (a running real instance is left alone) and MIKESHIFT_NO_HOOKS=1 (no global input hooks), and is always
  killed at the end.

.EXAMPLE
  ./scripts/capture-scene.ps1 -AppArgs '--scene','toast-success' -Out docs/screenshots/surfaces-v2/toast-success-dark.png -Title 'MikeShift'
  ./scripts/capture-scene.ps1 -AppArgs '--show-settings' -Theme light -Out settings-light.png -Title 'Settings'
#>
param(
    [Parameter(Mandatory)] [string[]] $AppArgs,
    [Parameter(Mandatory)] [string] $Out,
    [string] $Title = '',            # regex on the window title ('' = the largest visible window of the process)
    [ValidateSet('', 'dark', 'light')] [string] $Theme = '',
    [string] $Exe = '',
    [int] $SettleMs = 900,
    [int] $TimeoutMs = 15000,
    [string[]] $Invoke = @(),        # UIA: invoke (click/expand/toggle) the named elements, in order, before capturing
    [string[]] $Env = @(),           # extra NAME=VALUE environment variables for the process
    [switch] $FirstRun,              # keep the welcome window's first-run state (default: a scratch settings file with launched=true)
    [switch] $ScreenGrab,            # grab the screen rectangle instead of PrintWindow (shows DWM frame/corners/shadow; the pixels around the window are the user's desktop: inspect, do not commit)
    [int] $Zoom = 1,
    [switch] $Cloaked                # also accept cloaked windows (the toast reveals itself after its first frame)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$refs = @([System.Drawing.Bitmap].Assembly.Location, [System.Reflection.Assembly]::Load('System.Private.Windows.GdiPlus').Location, [System.Reflection.Assembly]::Load('System.Drawing.Primitives').Location, [System.Reflection.Assembly]::Load('System.Private.Windows.Core').Location)
Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
public static class Cap
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    public class Win { public IntPtr H; public string Title, Class; public int W, Hh, X, Y; public bool Cloaked; }

    public static void DpiAware() { SetProcessDpiAwarenessContext(new IntPtr(-4)); }

    public static Win[] Windows(uint pid, bool includeCloaked)
    {
        var list = new Win[0];
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            RECT r; GetWindowRect(h, out r);
            int cloaked; DwmGetWindowAttribute(h, 14, out cloaked, 4);
            if (!IsWindowVisible(h)) return true;
            if (cloaked != 0 && !includeCloaked) return true;
            var t = new StringBuilder(256); GetWindowText(h, t, 256);
            var c = new StringBuilder(256); GetClassName(h, c, 256);
            int w = r.R - r.L, hh = r.B - r.T;
            if (w < 40 || hh < 20 || r.L < -20000) return true;
            Array.Resize(ref list, list.Length + 1); list[list.Length - 1] = (new Win { H = h, Title = t.ToString(), Class = c.ToString(), W = w, Hh = hh, X = r.L, Y = r.T, Cloaked = cloaked != 0 });
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static string Dump(uint pid)
    {
        var sb = new StringBuilder();
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            RECT r; GetWindowRect(h, out r);
            var t = new StringBuilder(256); GetWindowText(h, t, 256);
            sb.Append("[" + t + " style=" + GetWindowLongPtr(h, -16).ToInt64().ToString("X") + " ex=" + GetWindowLongPtr(h, -20).ToInt64().ToString("X") + " vis=" + IsWindowVisible(h) + " " + (r.R - r.L) + "x" + (r.B - r.T) + "@" + r.L + "," + r.T + "] ");
            return true;
        }, IntPtr.Zero);
        return sb.ToString();
    }

    public static bool SaveScreen(IntPtr h, string path, int margin, int zoom)
    {
        RECT r; GetWindowRect(h, out r);
        int w = r.R - r.L + 2 * margin, hh = r.B - r.T + 2 * margin;
        using (var bmp = new Bitmap(w, hh, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.L - margin, r.T - margin, 0, 0, new Size(w, hh));
            using (var big = new Bitmap(w * zoom, hh * zoom))
            using (var g2 = Graphics.FromImage(big))
            {
                g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g2.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g2.DrawImage(bmp, 0, 0, w * zoom, hh * zoom);
                big.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        return true;
    }

    public static bool Save(IntPtr h, string path)
    {
        RECT r; GetWindowRect(h, out r);
        int w = r.R - r.L, hh = r.B - r.T;
        using (var bmp = new Bitmap(w, hh, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dc = g.GetHdc();
                bool ok = PrintWindow(h, dc, 2);
                g.ReleaseHdc(dc);
                if (!ok) return false;
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        return true;
    }
}
'@ -ReferencedAssemblies $refs

[Cap]::DpiAware()

if (-not $Exe) {
    $root = Split-Path $PSScriptRoot -Parent
    $Exe = Join-Path $root 'src\MikeShift.App\bin\x64\Debug\net10.0-windows10.0.26100.0\MikeShift.exe'
}
if (-not (Test-Path $Exe)) { throw "MikeShift.exe not found: $Exe (build first: dotnet build src/MikeShift.App -p:Platform=x64)" }

$psi = New-Object System.Diagnostics.ProcessStartInfo $Exe
$psi.UseShellExecute = $false
$psi.WorkingDirectory = Split-Path $Exe
foreach ($a in $AppArgs) { $psi.ArgumentList.Add($a) }
$psi.Environment['MIKESHIFT_NO_SINGLE_INSTANCE'] = '1'
$psi.Environment['MIKESHIFT_NO_HOOKS'] = '1'
if ($Theme) { $psi.Environment['MIKESHIFT_THEME'] = $Theme }
# A scratch settings file: the dev run never reads or writes the real preferences.
$settings = Join-Path ([System.IO.Path]::GetTempPath()) ("mikeshift-capture-" + [guid]::NewGuid().ToString('N') + '.json')
Set-Content -LiteralPath $settings -Value $(if ($FirstRun) { '{}' } else { '{"launched":"true"}' })
$psi.Environment['MIKESHIFT_SETTINGS_PATH'] = $settings
foreach ($e in $Env) { $k, $v = $e -split '=', 2; $psi.Environment[$k] = $v }

$proc = [System.Diagnostics.Process]::Start($psi)
try {
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
    $target = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 150
        if ($proc.HasExited) { throw "MikeShift exited early with code $($proc.ExitCode)" }
        $wins = [Cap]::Windows([uint32]$proc.Id, [bool]$Cloaked)
        if ($env:CAP_DEBUG) { Write-Host "pid $($proc.Id) wins $($wins.Length) : $([Cap]::Dump([uint32]$proc.Id))" }
        if ($Title) { $wins = @($wins | Where-Object { $_.Title -match $Title }) }
        if ($wins.Count -gt 0) { $target = $wins | Sort-Object { $_.W * $_.Hh } -Descending | Select-Object -First 1; break }
    }
    if (-not $target) {
        foreach ($w in [Cap]::Windows([uint32]$proc.Id, $true)) { Write-Host ("  seen: '{0}' [{1}] {2}x{3} at {4},{5} cloaked={6}" -f $w.Title, $w.Class, $w.W, $w.Hh, $w.X, $w.Y, $w.Cloaked) }
        throw "No matching window appeared within $TimeoutMs ms" }
    if ($env:CAP_POLL) { for ($i = 0; $i -lt 3; $i++) { Start-Sleep -Milliseconds 900; Write-Host ("t+{0}: {1}" -f (($i + 1) * 900), ([Cap]::Dump([uint32]$proc.Id) -replace '\[(Default IME|MSCTFIME UI|H.NotifyIcon_|MikeShift wheel)[^\]]*\] ?', '')) } }
    Start-Sleep -Milliseconds $SettleMs
    if ($env:CAP_UIA) {
        $rootEl = [System.Windows.Automation.AutomationElement]::RootElement
        $pc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $proc.Id
        foreach ($e in $rootEl.FindAll([System.Windows.Automation.TreeScope]::Children, $pc)) {
            $r = $e.Current.BoundingRectangle
            Write-Host ("UIA top-level: '{0}' class={1} type={2} rect={3},{4} {5}x{6} hwnd={7}" -f $e.Current.Name, $e.Current.ClassName, $e.Current.ControlType.ProgrammaticName, $r.X, $r.Y, $r.Width, $r.Height, $e.Current.NativeWindowHandle)
        }
    }

    # Drive our own controls through UI Automation patterns only (never mouse/keyboard injection).
    foreach ($name in $Invoke) {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($target.H)
        $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $name
        $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if (-not $el) { throw "UIA element '$name' not found" }
        $pat = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pat)) { $pat.Expand() }
        elseif ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) { $pat.Invoke() }
        elseif ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pat)) { $pat.Select() }
        elseif ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pat)) { $pat.Toggle() }
        else { throw "UIA element '$name' has no usable pattern" }
        Start-Sleep -Milliseconds 500
    }

    $full = [System.IO.Path]::GetFullPath($Out)
    $dir = Split-Path -Parent $full
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    if ($ScreenGrab) { [void][Cap]::SaveScreen($target.H, $full, 6, $Zoom) }
    elseif (-not [Cap]::Save($target.H, $full)) { throw 'PrintWindow failed' }
    Write-Host ("captured '{0}' [{1}] {2}x{3} -> {4}" -f $target.Title, $target.Class, $target.W, $target.Hh, $full)
}
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill($true); $proc.WaitForExit(3000) | Out-Null }
    if ($settings) { Remove-Item -LiteralPath $settings -Force -ErrorAction SilentlyContinue }
}
