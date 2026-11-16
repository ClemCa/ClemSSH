using System.Text;
using Renci.SshNet;

namespace ClemSSH.Client;

public static class InteractiveShell
{
    public static void Run(SshClient client)
    {
        uint columns = 80, rows = 24;
        if (!Console.IsOutputRedirected)
        {
            columns = (uint)Math.Max(1, Console.WindowWidth);
            rows = (uint)Math.Max(1, Console.WindowHeight);
        }

        using var shell = client.CreateShellStream("xterm", columns, rows, 0, 0, 65536);
        using var cancellation = new CancellationTokenSource();
        bool previousTreatControlC = Console.TreatControlCAsInput;
        if (!Console.IsInputRedirected)
            Console.TreatControlCAsInput = true;

        Task? reader = null;
        try
        {
            reader = Task.Run(() =>
            {
                try
                {
                    var output = Console.OpenStandardOutput();
                    var buffer = new byte[4096];
                    int count;
                    while ((count = shell.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, count);
                        output.Flush();
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                finally { cancellation.Cancel(); }
            });

            if (Console.IsInputRedirected)
            {
                var writer = Task.Run(() =>
                {
                    try
                    {
                        var input = Console.OpenStandardInput();
                        var buffer = new byte[4096];
                        int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            shell.Write(buffer, 0, count);
                            shell.Flush();
                        }
                    }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { }
                });
                Task.WaitAny(reader, writer);
            }
            else
            {
                while (!cancellation.IsCancellationRequested)
                {
                    if (!Console.KeyAvailable)
                    {
                        cancellation.Token.WaitHandle.WaitOne(20);
                        continue;
                    }
                    byte[] bytes = EncodeKey(Console.ReadKey(intercept: true));
                    if (bytes.Length == 0) continue;
                    shell.Write(bytes, 0, bytes.Length);
                    shell.Flush();
                }
            }
        }
        finally
        {
            shell.Dispose();
            reader?.GetAwaiter().GetResult();
            if (!Console.IsInputRedirected)
                Console.TreatControlCAsInput = previousTreatControlC;
        }
    }

    public static byte[] EncodeKey(ConsoleKeyInfo key)
    {
        string? sequence = key.Key switch
        {
            ConsoleKey.UpArrow => "\x1b[A",
            ConsoleKey.DownArrow => "\x1b[B",
            ConsoleKey.RightArrow => "\x1b[C",
            ConsoleKey.LeftArrow => "\x1b[D",
            ConsoleKey.Home => "\x1b[H",
            ConsoleKey.End => "\x1b[F",
            ConsoleKey.Insert => "\x1b[2~",
            ConsoleKey.Delete => "\x1b[3~",
            ConsoleKey.PageUp => "\x1b[5~",
            ConsoleKey.PageDown => "\x1b[6~",
            ConsoleKey.Backspace => "\x7f",
            ConsoleKey.Enter => "\r",
            _ => null
        };
        if (sequence != null) return Encoding.UTF8.GetBytes(sequence);
        if (key.KeyChar != '\0')
            return Encoding.UTF8.GetBytes(new[] { key.KeyChar });
        return [];
    }
}
