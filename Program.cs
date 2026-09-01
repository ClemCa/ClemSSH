/* ClemSSH - Easy-Auth SSH Client
*
* Any level of detail work on memorized hosts:
* clemssh user@host:port
* clemssh user@host
* clemssh host
*/

using System.Text.RegularExpressions;
using ClemSSH.Client;
using ClemSSH.Lib;

if (args.Length != 1)
{
    PrintHelp();
    return;
}
string fused = args.Length > 1 ? string.Join(" ", args[1..]) : "";
Regex regex = CommandRegex();

var match = regex.Match(args[0]);
if (!match.Success)
{
    PrintHelp();
    return;
}

string host = match.Groups["host"].Value;
string? user = match.Groups["user"].Success ? match.Groups["user"].Value : null;
int port = match.Groups["port"].Success ? int.Parse(match.Groups["port"].Value) : -1;

KeySearcher.Search(host, user, port, out List<string> matches);

if (matches.Count == 0)
{
    if (port == -1)
    {
        Console.WriteLine($"No port, defaulting to 22");
        port = 22;
    }
    if (user == null)
    {
        Console.WriteLine($"No user, defaulting to current user");
        user = Environment.UserName;
    }
    string password = "";
    while (password == "")
    {
        password = UI.Password("Password: ");
    }
    SSHClient.Open(host, port, user, password);
    return;
}

if (matches.Count > 1)
{
    int selected = UI.Select("Multiple matches found. Select one:", matches);
    if (selected == -1)
    {
        Console.WriteLine("Aborted.");
        return;
    }
    matches[0] = matches[selected];
}

if (!Store.Instance.LoadEncrypted(matches[0], out string? pass))
{
    Crasher.Crash($"Password for {matches[0]} not found in store.");
    return;
}

string[] parts = [..CommandRegex().Match(matches[0]).Groups.Values.Select(g => g.Value)];
SSHClient.Open(matches[0], int.Parse(parts[2]), parts[1], pass!);
return;

partial class Program
{
    [GeneratedRegex(@"^(?:(?<user>[^@]+)@)?(?<host>[^:]+)(?::(?<port>\d+))?$")]
    private static partial Regex CommandRegex();
    private static void PrintHelp()
    {
        Console.WriteLine("Usage: clemssh [user@]host[:port]");
        Console.WriteLine("Example: clemssh user@host:22");
        Console.WriteLine("Example: clemssh user@host");
        Console.WriteLine("Example: clemssh host");
    }
}