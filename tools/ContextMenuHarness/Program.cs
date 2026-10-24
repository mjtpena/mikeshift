namespace ContextMenuHarness;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: ContextMenuHarness probe|menu ...");
            return 2;
        }
        return args[0] switch
        {
            "probe" => ComProbe.Run(args[1..]),
            "menu" => MenuScenarios.Run(args[1..]),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("unknown mode");
        return 2;
    }
}
