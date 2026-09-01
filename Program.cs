/* ClemSSH - Easy-Auth SSH Client
*
* Any level of detail work on memorized hosts:
* clemssh [<user>@]<host>[:<port>]
*
* See saved hosts:
* clemssh (--list | -l)
* clemssh (--search | -s) [<user>@]<host>[:<port>]
* clemssh (--delete | -d) [<user>@]<host>[:<port>]
*/

using System.Text.RegularExpressions;
using ClemSSH.Client;
using ClemSSH.Lib;

if (args.Length != 1)
{
    if (args.Length == 2 && (args[0] == "--delete" || args[0] == "-d"))
    {
        string fusedDelete = args[1];
        Regex regexDelete = CommandRegex();
        var matchDelete = regexDelete.Match(fusedDelete);
        if (!matchDelete.Success)
        {
            PrintHelp();
            return;
        }
        string hostD = matchDelete.Groups["host"].Value;
        string? userD = matchDelete.Groups["user"].Success ? matchDelete.Groups["user"].Value : null;
        int portD = matchDelete.Groups["port"].Success ? int.Parse(matchDelete.Groups["port"].Value) : -1;

        KeySearcher.Search(hostD, userD, portD, out List<string> matchesD);
        if (matchesD.Count == 0)
        {
            Console.WriteLine("No matches found.");
            return;
        }
        if (matchesD.Count > 1)
        {
            int selected = UI.Select("Multiple matches found. Select one to delete:", matchesD);
            if (selected == -1)
            {
                Console.WriteLine("Aborted.");
                return;
            }
            matchesD[0] = matchesD[selected];
        }
        Store.Instance.DeleteEncrypted(matchesD[0]);
        Console.WriteLine($"Deleted {matchesD[0]}.");
        return;
    }
    if (args.Length == 2 && (args[0] == "--search" || args[0] == "-s"))
    {
        string fusedSearch = args[1];
        Regex regexSearch = CommandRegex();
        var matchSearch = regexSearch.Match(fusedSearch);
        if (!matchSearch.Success)
        {
            PrintHelp();
            return;
        }
        string hostS = matchSearch.Groups["host"].Value;
        string? userS = matchSearch.Groups["user"].Success ? matchSearch.Groups["user"].Value : null;
        int portS = matchSearch.Groups["port"].Success ? int.Parse(matchSearch.Groups["port"].Value) : -1;

        KeySearcher.Search(hostS, userS, portS, out List<string> matchesS);
        if (matchesS.Count == 0)
        {
            Console.WriteLine("No matches found.");
            return;
        }
        NavigateMatches("Matches:", matchesS);
        return;
    }
    PrintHelp();
    return;
}

if (args[0] == "--list" || args[0] == "-l")
{
    var keys = Store.Instance.Keys;
    if (keys.Count == 0)
    {
        Console.WriteLine("No saved hosts.");
        return;
    }
    NavigateMatches("All saved hosts:", [..keys]);
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
    SSHClient.Open(host, port, user, password, savePassword: true);
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

string[] parts = [.. CommandRegex().Match(matches[0]).Groups.Values.Select(g => g.Value)];
SSHClient.Open(matches[0], int.Parse(parts[2]), parts[1], pass!, savePassword: false);
return;

partial class Program
{
    [GeneratedRegex(@"^(?:(?<user>[^@]+)@)?(?<host>[^:]+)(?::(?<port>\d+))?$")]
    private static partial Regex CommandRegex();
    private static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  clemssh [<user>@]<host>[:<port>] - Connect to a host");
        Console.WriteLine("  clemssh (--list | -l) - List & manage saved hosts");
        Console.WriteLine("  clemssh (--search | -s) [<user>@]<host>[:<port>] - List & manage saved hosts by search");
        Console.WriteLine("  clemssh (--delete | -d) [<user>@]<host>[:<port>] - Delete a saved host");
    }
    private static void NavigateMatches(string title, List<string> matches, string preSelected = "")
    {
        string selectedKey;
        if (preSelected == "")
        {
            int selected = UI.Select(title, matches);
            if (selected == -1)
            {
                return;
            }
            selectedKey = matches[selected];
        } else
        {
            selectedKey = preSelected;
        }
        int selection = UI.Select(selectedKey, new List<string> { "Connect", "Delete", "Cancel" }, defaultIndex: 0);
        if (selection == -1 || selection == 2)
        {
            NavigateMatches(title, matches);
            return;
        }
        if (selection == 1)
        {
            int confirm = UI.Select($"Are you sure you want to delete {selectedKey}?", new List<string> { "Yes", "No" }, defaultIndex: 1);
            if (confirm != 0)
            {
                NavigateMatches(title, matches, preSelected: selectedKey);
                return;
            }
            Store.Instance.DeleteEncrypted(selectedKey);
            Console.WriteLine($"Deleted {selectedKey}");
            NavigateMatches(title, [..matches.Where(m => m != selectedKey)]);
            return;
        }
        // 0 - Connect
        string[] parts = [..CommandRegex().Match(selectedKey).Groups.Values.Select(g => g.Value)];
        if (!Store.Instance.LoadEncrypted(selectedKey, out string? pass))
        {
            Crasher.Crash($"Password for {selectedKey} not found in store.");
            return;
        }
        SSHClient.Open(parts[0], int.Parse(parts[2]), parts[1], pass!, savePassword: false);
    }
}