using System.Text;
using Shiftvert.Core.Cli;

// Thin entry point; everything lives in Shiftvert.Core.Cli.CliRunner so it can be tested in-process.
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

return await CliRunner.RunAsync(args, stdout, stderr, ct: cts.Token);
