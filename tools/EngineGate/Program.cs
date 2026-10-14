namespace MtgArchive.Tools.EngineGate;

public static class Program
{
    public static Task<int> Main(string[] args) => Cli.RunAsync(args, Console.Out, Console.Error);
}
