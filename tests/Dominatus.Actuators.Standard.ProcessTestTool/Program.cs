namespace Dominatus.Actuators.Standard.ProcessTestTool;

public static class ProcessTestToolMarker;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
            return 1;

        return args[0] switch
        {
            "stdout" => WriteStdout(args),
            "stderr" => WriteStderr(args),
            "exit" => ExitCode(args),
            "sleep" => Sleep(args),
            "cwd" => PrintCwd(),
            "env" => PrintEnv(args),
            _ => 2
        };
    }

    private static int WriteStdout(string[] args)
    {
        Console.Out.Write(args.Length > 1 ? args[1] : string.Empty);
        return 0;
    }

    private static int WriteStderr(string[] args)
    {
        Console.Error.Write(args.Length > 1 ? args[1] : string.Empty);
        return 0;
    }

    private static int ExitCode(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var code))
            return 3;

        return code;
    }

    private static int Sleep(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var ms))
            return 4;

        Thread.Sleep(ms);
        return 0;
    }

    private static int PrintCwd()
    {
        Console.Out.Write(Directory.GetCurrentDirectory());
        return 0;
    }

    private static int PrintEnv(string[] args)
    {
        if (args.Length < 2)
            return 5;

        Console.Out.Write(Environment.GetEnvironmentVariable(args[1]) ?? string.Empty);
        return 0;
    }
}
