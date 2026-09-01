using System.Diagnostics;
using ClemSSH.Lib;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace ClemSSH.Client;

public static class SSHClient
{
    // we don't actually carea bout the SSH client,
    // our purpose is to handle the auth and make it easier
    // we can forward it all, we won't do interactivity better anyway
    public static void Open(string host, int port, string username, string password, bool savePassword)
    {
        var connectionInfo = new ConnectionInfo(
            host,
            port,
            username,
            new PasswordAuthenticationMethod(username, password)
        );
        using var client = new SshClient(connectionInfo);
        try
        {
            client.Connect();
            if (client.IsConnected && savePassword) {
                // save password
                Store.Instance.SaveEncrypted($"{username}@{host}:{port}", password);
            }
            InteractiveShell.Run(client);
            client.Disconnect();
        } catch (SshAuthenticationException e)
        {
            Console.Error.WriteLine($"Authentication failed: {e.Message}");
        }
        catch (SshConnectionException e)
        {
            Console.Error.WriteLine($"Connection failed: {e.Message}");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Error: {e.Message}");
        }
    }
}