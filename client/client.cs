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
    public static void Open(string host, int port, string username, string? password, bool savePassword)
    {
        string? enteredPassword = null;
        string GetPassword()
        {
            if (password != null) return password;
            if (enteredPassword != null) return enteredPassword;
            Console.WriteLine($"The SSH server requests a password for {username}@{host}.");
            return enteredPassword = UI.PasswordSilent("Password: ");
        }
        var keyboard = new KeyboardInteractiveAuthenticationMethod(username);
        keyboard.AuthenticationPrompt += (_, args) =>
        {
            foreach (var prompt in args.Prompts)
            {
                if (!prompt.IsEchoed && prompt.Request.Contains("password", StringComparison.OrdinalIgnoreCase))
                    prompt.Response = GetPassword();
                else
                    prompt.Response = prompt.IsEchoed ? UI.Input(prompt.Request) : UI.PasswordSilent(prompt.Request);
            }
        };
        var connectionInfo = new ConnectionInfo(
            host,
            port,
            username,
            new PromptingPasswordAuthenticationMethod(username, GetPassword),
            keyboard
        );
        using var client = new SshClient(connectionInfo);
        try
        {
            client.Connect();
            if (client.IsConnected && savePassword && enteredPassword != null && UI.Confirm("Save this SSH password?", defaultYes: false)) {
                Store.Instance.SaveEncrypted($"{username}@{host}:{port}", enteredPassword);
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

    private sealed class PromptingPasswordAuthenticationMethod(string username, Func<string> getPassword) : AuthenticationMethod(username)
    {
        public override string Name => "password";

        public override AuthenticationResult Authenticate(Session session)
        {
            using var method = new PasswordAuthenticationMethod(Username, getPassword());
            var result = method.Authenticate(session);
            AllowedAuthentications = method.AllowedAuthentications;
            return result;
        }
    }
}
