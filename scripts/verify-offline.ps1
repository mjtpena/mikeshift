<#
.SYNOPSIS
  Runtime proof that MikeShift stays on the machine: runs the complete conversion matrix through the real CLI and a MikeShift.exe
  session while polling every 50 ms for any TCP connection, TCP listener or UDP endpoint owned by the MikeShift processes.

.DESCRIPTION
  Phases
    1. Samples: writes one file of every kind (images, PDFs, audio, video, documents, a folder) with the test project's fixture
       generators (tests/MikeShift.Core.Tests/Integrity/IntegrityFixtureExport.cs).
    2. CLI matrix: for each sample, `mikeshift --list`, then `mikeshift --run <title>` for EVERY action offered (Crop and Trim get
       --crop / --range; Merge, Merge PDF get two inputs). Each run is a separate mikeshift.exe process.
    3. App session: MikeShift.exe (MIKESHIFT_NO_SINGLE_INSTANCE=1) idles for -IdleSeconds, then a second instance is launched with
       `--convert <samples>` (the click-mode wheel) and an Enter key press picks the first tile; then the apps are closed.
    4. Word: Microsoft Word is OFF by default (Settings "Use Microsoft Word for Word documents", CLI `--use-word`), so this run, which
       never passes --use-word, must not start Word at all. WINWORD.EXE stays in the watch list: any Word process started during the run
       is reported (and fails the run), together with whatever sockets it owned, apart from MikeShift's own. Add -UseWord to measure the
       opt-in path instead (every CLI run gets --use-word; legacy .doc and DOCX to PDF then go through Word, which connects to Microsoft).

  What is watched (no administrator rights, no OS setting is changed):
    * A native sampler (iphlpapi GetExtendedTcpTable / GetExtendedUdpTable, IPv4 and IPv6, every TCP state including LISTEN) polls
      every -PollMs (50 ms) for the PIDs of processes named mikeshift, MikeShift, MikeShift.Cli (and WINWORD, reported separately).
      These are the tables Get-NetTCPConnection and Get-NetUDPEndpoint read, without their per-call cost, so 50 ms is achievable.
    * As a cross-check, Get-NetTCPConnection -OwningProcess and Get-NetUDPEndpoint -OwningProcess are polled in a second thread.
    * Loaded modules of the MikeShift processes are sampled and matched against networking libraries (ws2_32, winhttp, wininet,
      dnsapi, System.Net.*, PdfSharp.Quality, PdfSharp.Cryptography, WebView2 ...).
    * DNS: the DNS client cache (Get-DnsClientCache) is compared before and after. It is machine wide and has no per-process
      attribution; the DNS-Client ETW/event log needs administrator rights to enable, so it is not used.

  The result is written as JSON (-ReportPath) and summarised on screen. Exit code 0 when no MikeShift process owned any
  connection or endpoint and (without -UseWord) no Word process was started, 1 otherwise.

.PARAMETER SkipApp      Do not start MikeShift.exe.
.PARAMETER IdleSeconds  How long the app idles before the --convert step (default 60).
.PARAMETER PollMs       Sampling interval (default 50).
.PARAMETER KeepWork     Keep the work folder (samples and outputs) for inspection.
.PARAMETER UseWord      Pass --use-word to every CLI run (the opt-in path). Word processes are then expected and only reported.
#>
[CmdletBinding()]
param(
  [string]$CliPath = (Join-Path $PSScriptRoot '..\src\MikeShift.Cli\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\mikeshift.exe'),
  [string]$AppPath = (Join-Path $PSScriptRoot '..\src\MikeShift.App\bin\x64\Debug\net10.0-windows10.0.26100.0\MikeShift.exe'),
  [string]$ReportPath = (Join-Path $PSScriptRoot '..\artifacts\verify-offline.json'),
  [string]$WorkDir = (Join-Path ([IO.Path]::GetTempPath()) ('mikeshift-offline-' + [Guid]::NewGuid().ToString('N'))),
  [int]$IdleSeconds = 60,
  [int]$PollMs = 50,
  [switch]$SkipApp,
  [switch]$UseWord,
  [switch]$KeepWork
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$CliPath = (Resolve-Path $CliPath).Path
if (-not $SkipApp) { $AppPath = (Resolve-Path $AppPath).Path }

Add-Type -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

public sealed class NetHit
{
    public string Proto, Local, Remote, State, Process; public int Pid; public string FirstSeen;
}

public sealed class NetPoller
{
    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int af, int cls, uint reserved);
    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool sort, int af, int cls, uint reserved);

    const int AF_INET = 2, AF_INET6 = 23, TCP_TABLE_OWNER_PID_ALL = 5, UDP_TABLE_OWNER_PID = 1;
    static readonly string[] TcpStates = { "", "CLOSED", "LISTEN", "SYN_SENT", "SYN_RCVD", "ESTABLISHED", "FIN_WAIT1", "FIN_WAIT2", "CLOSE_WAIT", "CLOSING", "LAST_ACK", "TIME_WAIT", "DELETE_TCB" };
    static readonly Regex NetModules = new Regex(@"^(ws2_32|wsock32|winhttp|wininet|dnsapi|mswsock|webservices|urlmon|msquic|System\.Net\..*|System\.Private\.Uri|PdfSharp\.Quality|PdfSharp\.Cryptography|Microsoft\.Web\.WebView2.*|WebView2Loader.*|Microsoft\.Windows\.PushNotifications.*)\.dll$", RegexOptions.IgnoreCase);

    readonly string[] _watch;
    readonly int _intervalMs;
    Thread _thread;
    volatile bool _stop;

    public readonly ConcurrentDictionary<string, NetHit> Hits = new ConcurrentDictionary<string, NetHit>();
    public readonly ConcurrentDictionary<int, string> PidsSeen = new ConcurrentDictionary<int, string>();
    public readonly ConcurrentDictionary<string, string> NetworkModules = new ConcurrentDictionary<string, string>();
    public long Samples;

    public NetPoller(string[] processNames, int intervalMs) { _watch = processNames; _intervalMs = intervalMs; }

    public void Start() { _thread = new Thread(Loop) { IsBackground = true, Name = "NetPoller" }; _thread.Start(); }
    public void Stop() { _stop = true; _thread.Join(); }

    void Loop()
    {
        var sw = Stopwatch.StartNew();
        long tick = 0;
        while (!_stop)
        {
            var started = sw.ElapsedMilliseconds;
            var pids = new Dictionary<int, string>();
            foreach (var name in _watch)
                foreach (var p in Process.GetProcessesByName(name))
                {
                    pids[p.Id] = p.ProcessName;
                    PidsSeen.TryAdd(p.Id, p.ProcessName);
                    if (tick % 5 == 0 && !p.ProcessName.Equals("WINWORD", StringComparison.OrdinalIgnoreCase))
                    {
                        try { foreach (ProcessModule m in p.Modules) if (NetModules.IsMatch(m.ModuleName)) NetworkModules.TryAdd(p.ProcessName + ":" + m.ModuleName, m.FileName); }
                        catch (Exception) { /* exited or access denied */ }
                    }
                    p.Dispose();
                }
            if (pids.Count > 0) Scan(pids);
            Interlocked.Increment(ref Samples);
            tick++;
            var wait = _intervalMs - (int)(sw.ElapsedMilliseconds - started);
            if (wait > 0) Thread.Sleep(wait);
        }
    }

    void Scan(Dictionary<int, string> pids)
    {
        foreach (var af in new[] { AF_INET, AF_INET6 })
        {
            var a = af;
            var tcp = ReadTable((ptr, sz) => { int s = sz; var r = GetExtendedTcpTable(ptr, ref s, false, a, TCP_TABLE_OWNER_PID_ALL, 0); lastSize = s; return r; }, out var tc);
            var rowT = af == AF_INET ? 24 : 56;
            for (var i = 0; i < tc; i++)
            {
                var o = 4 + i * rowT;
                int pid, lp, rp, state; string la, ra;
                if (af == AF_INET)
                {
                    state = BitConverter.ToInt32(tcp, o); la = new IPAddress(BitConverter.ToUInt32(tcp, o + 4)).ToString(); lp = Port(tcp, o + 8);
                    ra = new IPAddress(BitConverter.ToUInt32(tcp, o + 12)).ToString(); rp = Port(tcp, o + 16); pid = BitConverter.ToInt32(tcp, o + 20);
                }
                else
                {
                    la = new IPAddress(tcp.Skip(o).Take(16).ToArray()).ToString(); lp = Port(tcp, o + 20);
                    ra = new IPAddress(tcp.Skip(o + 24).Take(16).ToArray()).ToString(); rp = Port(tcp, o + 44);
                    state = BitConverter.ToInt32(tcp, o + 48); pid = BitConverter.ToInt32(tcp, o + 52);
                }
                if (pids.TryGetValue(pid, out var name))
                    Add("TCP" + (af == AF_INET ? "4" : "6"), la + ":" + lp, ra + ":" + rp, state >= 0 && state < TcpStates.Length ? TcpStates[state] : state.ToString(), pid, name);
            }
            var udp = ReadTable((ptr, sz) => { int s = sz; var r = GetExtendedUdpTable(ptr, ref s, false, a, UDP_TABLE_OWNER_PID, 0); lastSize = s; return r; }, out var uc);
            var rowU = af == AF_INET ? 12 : 28;
            for (var i = 0; i < uc; i++)
            {
                var o = 4 + i * rowU;
                string la; int lp, pid;
                if (af == AF_INET) { la = new IPAddress(BitConverter.ToUInt32(udp, o)).ToString(); lp = Port(udp, o + 4); pid = BitConverter.ToInt32(udp, o + 8); }
                else { la = new IPAddress(udp.Skip(o).Take(16).ToArray()).ToString(); lp = Port(udp, o + 20); pid = BitConverter.ToInt32(udp, o + 24); }
                if (pids.TryGetValue(pid, out var name))
                    Add("UDP" + (af == AF_INET ? "4" : "6"), la + ":" + lp, "*", "", pid, name);
            }
        }
    }

    [ThreadStatic] static int lastSize;

    static byte[] ReadTable(Func<IntPtr, int, uint> call, out int count)
    {
        int size = 0;
        lastSize = 0;
        call(IntPtr.Zero, 0);
        size = Math.Max(lastSize, 64) + 1024;
        for (var i = 0; i < 6; i++)
        {
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                var rc = call(buf, size);
                if (rc == 0) { var bytes = new byte[size]; Marshal.Copy(buf, bytes, 0, size); count = BitConverter.ToInt32(bytes, 0); return bytes; }
                if (rc != 122) break;
                size = Math.Max(lastSize, size) + 4096;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        count = 0; return new byte[4];
    }

    static int Port(byte[] b, int o) => (b[o] << 8) | b[o + 1];

    void Add(string proto, string local, string remote, string state, int pid, string name)
    {
        var key = proto + "|" + local + "|" + remote + "|" + state + "|" + pid;
        Hits.TryAdd(key, new NetHit { Proto = proto, Local = local, Remote = remote, State = state, Pid = pid, Process = name, FirstSeen = DateTime.Now.ToString("HH:mm:ss.fff") });
    }
}
'@

function Invoke-Cli {
  param([string[]]$Arguments)
  $psi = [Diagnostics.ProcessStartInfo]::new($CliPath)
  $psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.CreateNoWindow = $true
  $psi.StandardOutputEncoding = [Text.Encoding]::UTF8; $psi.StandardErrorEncoding = [Text.Encoding]::UTF8
  if ($UseWord) { $psi.ArgumentList.Add('--use-word') }
  foreach ($a in $Arguments) { $psi.ArgumentList.Add($a) }
  $p = [Diagnostics.Process]::Start($psi)
  $out = $p.StandardOutput.ReadToEndAsync(); $err = $p.StandardError.ReadToEndAsync()
  if (-not $p.WaitForExit(180000)) { $p.Kill($true); return [pscustomobject]@{ Exit = -1; Out = ''; Err = 'timeout' } }
  [pscustomobject]@{ Exit = $p.ExitCode; Out = $out.Result; Err = $err.Result }
}

$started = Get-Date
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$samples = Join-Path $WorkDir 'samples'
New-Item -ItemType Directory -Force -Path $samples | Out-Null
"Work folder: $WorkDir"

# ---- Phase 1: samples ------------------------------------------------------------------------------------------------
"Writing sample files ..."
Push-Location $root
try {
  $env:MIKESHIFT_INTEGRITY_FIXTURES_OUT = $samples
  dotnet test tests/MikeShift.Core.Tests --nologo --filter "FullyQualifiedName~IntegrityFixtureExport" | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "sample generation failed" }
} finally { Remove-Item Env:\MIKESHIFT_INTEGRITY_FIXTURES_OUT -ErrorAction SilentlyContinue; Pop-Location }
$files = Get-ChildItem $samples -Recurse -File | Where-Object { $_.FullName -notmatch '\\folder\\' }
"{0} sample files + the folder" -f $files.Count

# ---- before-snapshot: DNS cache -------------------------------------------------------------------------------------------
$dnsBefore = @{}
try { Get-DnsClientCache -ErrorAction Stop | ForEach-Object { $dnsBefore[$_.Entry] = $true } } catch { "DNS cache not readable: $($_.Exception.Message)" }

# A Word you already had open is not ours to measure: only WINWORD processes started during the run count.
$preexistingWord = @(Get-Process -Name WINWORD -ErrorAction SilentlyContinue | ForEach-Object Id)
if ($preexistingWord.Count) { "Ignoring $($preexistingWord.Count) WINWORD process(es) that were already running." }

# ---- watchers ------------------------------------------------------------------------------------------------------------
$poller = [NetPoller]::new(@('mikeshift', 'MikeShift', 'MikeShift.Cli', 'WINWORD'), $PollMs)
$cmdletLog = [System.Collections.Concurrent.ConcurrentDictionary[string, string]]::new()
$cmdletStop = [hashtable]::Synchronized(@{ Stop = $false; Rounds = 0 })
$cmdletJob = Start-ThreadJob -ArgumentList $cmdletLog, $cmdletStop -ScriptBlock {
  param($log, $ctl)
  while (-not $ctl.Stop) {
    $ids = @(Get-Process -Name mikeshift, MikeShift, MikeShift.Cli, WINWORD -ErrorAction SilentlyContinue | ForEach-Object Id)
    if ($ids.Count) {
      foreach ($c in @(Get-NetTCPConnection -OwningProcess $ids -ErrorAction SilentlyContinue)) { [void]$log.TryAdd("TCP $($c.LocalAddress):$($c.LocalPort) -> $($c.RemoteAddress):$($c.RemotePort) $($c.State) pid $($c.OwningProcess)", '') }
      foreach ($u in @(Get-NetUDPEndpoint -OwningProcess $ids -ErrorAction SilentlyContinue)) { [void]$log.TryAdd("UDP $($u.LocalAddress):$($u.LocalPort) pid $($u.OwningProcess)", '') }
    }
    $ctl.Rounds++
    Start-Sleep -Milliseconds 200
  }
}
$poller.Start()

$cliRuns = 0; $exitCodes = @{}; $failures = New-Object System.Collections.Generic.List[string]
$cliPids = 0
try {
  # ---- Phase 2: CLI matrix ---------------------------------------------------------------------------------------------------
  "CLI matrix ..."
  $wordTitles = @()
  foreach ($f in $files) {
    $list = Invoke-Cli @('--list', $f.FullName); $cliRuns++
    if ($list.Exit -ne 0) { $failures.Add("--list $($f.Name): exit $($list.Exit)"); continue }
    $titles = @($list.Out.Trim() -split ', ' | Where-Object { $_ })
    foreach ($t in $titles) {
      if ($t -in 'Merge', 'Merge PDF') { continue }      # need two inputs, run once per folder below
      $args = @('--run', $t)
      if ($t -eq 'Trim') { $args += @('--range', '0:00.5-0:01.5') }
      if ($t -eq 'Crop') { $args += @('--crop', '2,2,20,16') }
      $args += $f.FullName
      $r = Invoke-Cli $args; $cliRuns++
      $key = "exit $($r.Exit)"; $exitCodes[$key] = 1 + [int]$exitCodes[$key]
      if ($r.Exit -ne 0 -and $r.Err -notmatch 'already small|Animated images|pages, and only|no audio|probably a scan|find any text|No faces found|read from an animated|Convert it to PDF first') { $failures.Add("$($f.Name) / $t : exit $($r.Exit): $($r.Err.Trim())") }
    }
  }
  foreach ($set in @(@('Merge', (Get-ChildItem (Join-Path $samples 'pdf') -Filter *.pdf)), @('Merge PDF', (Get-ChildItem (Join-Path $samples 'images') -Include *.png, *.jpg -Recurse)))) {
    $r = Invoke-Cli (@('--run', $set[0]) + @($set[1] | Select-Object -First 3 | ForEach-Object FullName)); $cliRuns++
    $key = "exit $($r.Exit)"; $exitCodes[$key] = 1 + [int]$exitCodes[$key]
  }
  $zipFolder = Join-Path $samples 'folder\Project'
  $r = Invoke-Cli @('--run', 'Zip', $zipFolder); $cliRuns++
  $key = "exit $($r.Exit)"; $exitCodes[$key] = 1 + [int]$exitCodes[$key]
  "{0} CLI processes run" -f $cliRuns

  # ---- Phase 3: app session --------------------------------------------------------------------------------------------------
  $appSummary = 'skipped'
  if (-not $SkipApp) {
    "App session: $IdleSeconds s idle, then --convert ..."
    $env:MIKESHIFT_NO_SINGLE_INSTANCE = '1'
    $env:MIKESHIFT_NO_HOOKS = '1'      # no global mouse/keyboard hook: this is a measurement, not a UI test
    $app = Start-Process -FilePath $AppPath -PassThru
    Start-Sleep -Seconds 8
    $alive = -not $app.HasExited
    if ($alive) { Start-Sleep -Seconds $IdleSeconds }
    $converted = 'not attempted'
    if ($alive -and -not $app.HasExited) {
      $png = Join-Path $samples 'images\scene.png'
      $before = @(Get-ChildItem (Split-Path $png) -File).Count
      $second = Start-Process -FilePath $AppPath -ArgumentList @('--convert', $png) -PassThru
      Start-Sleep -Seconds 6
      try {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.SendKeys]::SendWait('{RIGHT}{ENTER}')   # click-mode wheel: arrow key to a tile, Enter to run it
      } catch { "key press failed: $($_.Exception.Message)" }
      Start-Sleep -Seconds 10
      $after = @(Get-ChildItem (Split-Path $png) -File).Count
      $converted = if ($after -gt $before) { "yes ($($after - $before) new file(s))" } else { 'no output appeared (the key press did not select a tile)' }
      if (-not $second.HasExited) { Stop-Process -Id $second.Id -Force }
    }
    if (-not $app.HasExited) { Stop-Process -Id $app.Id -Force }
    Remove-Item Env:\MIKESHIFT_NO_SINGLE_INSTANCE, Env:\MIKESHIFT_NO_HOOKS -ErrorAction SilentlyContinue
    $appSummary = "started: $alive; idle $IdleSeconds s; --convert conversion: $converted"
  }
}
finally {
  Start-Sleep -Milliseconds 500
  $poller.Stop()
  $cmdletStop.Stop = $true
  Receive-Job $cmdletJob -Wait -AutoRemoveJob -ErrorAction SilentlyContinue | Out-Null
}

# ---- after-snapshot: DNS cache ----------------------------------------------------------------------------------------------
$dnsAdded = @()
try { $dnsAdded = @(Get-DnsClientCache -ErrorAction Stop | Where-Object { -not $dnsBefore.ContainsKey($_.Entry) } | ForEach-Object { $_.Entry } | Sort-Object -Unique) } catch { }

# ---- results -------------------------------------------------------------------------------------------------------------------
$hits = @($poller.Hits.Values | Where-Object { $_.Process -ne 'WINWORD' -or $preexistingWord -notcontains $_.Pid })
$productHits = @($hits | Where-Object { $_.Process -ne 'WINWORD' })
$wordHits = @($hits | Where-Object { $_.Process -eq 'WINWORD' })
$observed = @($poller.PidsSeen.GetEnumerator() | ForEach-Object { [pscustomobject]@{ Pid = $_.Key; Process = $_.Value } }) + @($hits | ForEach-Object { [pscustomobject]@{ Pid = $_.Pid; Process = $_.Process } })
$pidsByName = $observed | Where-Object { $preexistingWord -notcontains $_.Pid } | Sort-Object Pid -Unique | Group-Object Process | ForEach-Object { [pscustomobject]@{ Process = $_.Name; DistinctPidsObserved = $_.Count } }
$wordStarted = @($poller.PidsSeen.GetEnumerator() | Where-Object { $_.Value -eq 'WINWORD' -and $preexistingWord -notcontains $_.Key }).Count
$result = [pscustomobject]@{
  UseWordFlag = [bool]$UseWord
  WinwordProcessesStartedDuringTheRun = $wordStarted
  Started = $started.ToString('s'); Finished = (Get-Date).ToString('s')
  PollMs = $PollMs; PollerSamples = $poller.Samples
  ProcessesObserved = @($pidsByName)
  CliProcessesStarted = $cliRuns
  CliExitCodes = $exitCodes
  UnexpectedCliFailures = @($failures)
  AppSession = $appSummary
  MikeShiftConnectionsOrEndpointsSeen = $productHits.Count
  MikeShiftHits = @($productHits)
  WinwordConnectionsOrEndpointsSeen = $wordHits.Count
  WinwordHits = @($wordHits)
  CmdletCrossCheckRounds = $cmdletStop.Rounds
  CmdletCrossCheckHits = @($cmdletLog.Keys | Where-Object { $k = $_; -not ($preexistingWord | Where-Object { $k -match "pid $_$" }) })
  NetworkingModulesLoadedByMikeShift = @($poller.NetworkModules.Keys | Sort-Object)
  DnsCacheEntriesAddedDuringTheRun = $dnsAdded
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
$result | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 $ReportPath

""
"=== Result ==="
"Polling every $PollMs ms: $($poller.Samples) samples over $([int]((Get-Date) - $started).TotalSeconds) s"
$pidsByName | Format-Table -AutoSize | Out-String | Write-Host
"CLI processes started: $cliRuns   exit codes: $(($exitCodes.GetEnumerator() | ForEach-Object { '{0}={1}' -f $_.Key, $_.Value }) -join ', ')"
"App session: $appSummary"
"TCP/UDP connections or endpoints owned by MikeShift processes: $($productHits.Count)"
$productHits | Format-Table Proto, Local, Remote, State, Process, Pid -AutoSize | Out-String | Write-Host
"WINWORD.EXE processes started during the run: $wordStarted" + $(if ($UseWord) { ' (--use-word was passed)' } else { ' (Word is off by default: must be 0)' })
"Owned by WINWORD.EXE (Microsoft Word, a separate product): $($wordHits.Count)"
$wordHits | Format-Table Proto, Local, Remote, State, Pid -AutoSize | Out-String | Write-Host
"Cmdlet cross-check (Get-NetTCPConnection/Get-NetUDPEndpoint, $($cmdletStop.Rounds) rounds): $($cmdletLog.Count) entries"
"Networking-related modules loaded by MikeShift processes: " + $(if ($poller.NetworkModules.Count) { ($poller.NetworkModules.Keys | Sort-Object) -join ', ' } else { '(none)' })
"DNS cache entries added during the run (machine wide, not attributable): " + $(if ($dnsAdded.Count) { $dnsAdded -join ', ' } else { '(none)' })
if ($failures.Count) { "Unexpected CLI failures:"; $failures | ForEach-Object { "  $_" } }
"Report: $ReportPath"

if (-not $KeepWork) { Remove-Item -Recurse -Force $WorkDir -ErrorAction SilentlyContinue }
exit ([int]($productHits.Count -gt 0 -or (-not $UseWord -and $wordStarted -gt 0)))
