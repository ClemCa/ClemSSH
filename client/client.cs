using System.Diagnostics;
using Renci.SshNet;

namespace ClemSSH.Client;

public static class SSHClient
{
    // we don't actually carea bout the SSH client,
    // our purpose is to handle the auth and make it easier
    // we can forward it all, we won't do interactivity better anyway
    public static void Open(string host, int port, string username, string password) {
        var connectionInfo = new ConnectionInfo(
            host,
            port,
            username,        
            new PasswordAuthenticationMethod(username, password)
        );
        using var client = new SshClient(connectionInfo);
        client.Connect();
        InteractiveShell.Run(client);
        client.Disconnect();
    }
}