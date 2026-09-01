// clean crash messages

namespace ClemSSH.Lib;

public static class Crasher
{
    public static void Crash(string message, int exitCode = 1)
    {
        Console.Error.WriteLine($"Panic: {message}");
        Console.Error.WriteLine("Press any key to exit...");
        try
        {
            Console.ReadKey(intercept: true);
        }
        catch (Exception) { }
        Environment.Exit(exitCode);
    }
}