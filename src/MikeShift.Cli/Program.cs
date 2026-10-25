using MikeShift.Cli;
using System.Text;
using MikeShift.Core.Cli;

// Thin entry point; everything lives in MikeShift.Core.Cli.CliRunner so it can be tested in-process.
var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.OutputEncoding = utf8;
using var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
using var stderr = new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true };

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var exitCode = await CliRunner.RunAsync(args, stdout, stderr, ct: cts.Token);

// Skip native teardown (see FastExit); if the terminate call itself fails, fall back to a normal exit.
FastExit.Now(exitCode, stdout, stderr);
return exitCode;
