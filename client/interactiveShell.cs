using Renci.SshNet;
using Renci.SshNet.Common;

namespace ClemSSH.Client;

public static class InteractiveShell
{
    public static void Run(SshClient client)
    {
        using var shell = client.CreateShellStream(
            "xterm", 80, 24, 800, 600, 65536);

        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            // forward ctrl+c to remote shell
            e.Cancel = true;
        };

        // pipe remote shell output to console
        var reader = Task.Run(() =>
        {
            try
            {
                while (shell.CanRead && !cancellation.IsCancellationRequested)
                {
                    string? chunk = shell.Read();
                    if (chunk == null) break;
                    Console.Write(chunk);
                }
            }
            catch (Exception) { /* stream closed */ }
        }, cancellation.Token);

        // pipe keyboard inputs to remote shell
        var stdin = Console.OpenStandardInput();
        var writer = Task.Run(() =>
        {
            var buf = new byte[4096];
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    int read = stdin.Read(buf, 0, buf.Length);
                    if (read <= 0) break;
                    shell.Write(buf, 0, read);
                    shell.Flush();
                }
            }
            catch (Exception) { /* stream closed */ }
        }, cancellation.Token);

        Task.WaitAny(reader, writer);
        cancellation.Cancel();
    }
}
