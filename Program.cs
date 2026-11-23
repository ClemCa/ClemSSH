/* ClemSSH - Easy-Auth SSH Client
*
* Open the interactive menu:
* clemssh
*
* Any level of detail work on memorized hosts:
* clemssh [<user>@]<host>[:<port>]
*
* See saved hosts:
* clemssh (--list | -l)
* clemssh (--search | -s) [<user>@]<host>[:<port>]
* clemssh (--delete | -d) [<user>@]<host>[:<port>]
*/

using ClemSSH.Client;
using ClemSSH.Lib;

if (args.Length == 0)
{
    if (Console.IsInputRedirected || Console.IsOutputRedirected)
    {
        PrintHelp();
    }
    else
    {
        RunHomeMenu();
    }
    return;
}

if (args[0] == "--alias")
{
    Environment.ExitCode = RunAliasCommand(args[1..]);
    return;
}

if (args.Length != 1)
{
    if (args.Length == 2 && (args[0] is "--delete" or "-d" or "--search" or "-s") &&
        SshDestination.TryParse(args[1], out var query))
    {
        var matches = KeySearcher.FindMatches(query, Store.Instance.Keys);
        if (matches.Count == 0)
        {
            Console.WriteLine("No matches found.");
            return;
        }
        if (args[0] is "--search" or "-s")
        {
            NavigateMatches("Matches:", matches);
            return;
        }
        int selected = matches.Count == 1 ? 0 : UI.Select("Multiple matches found. Select one to delete:", matches);
        if (selected == -1)
        {
            Console.WriteLine("Aborted.");
            return;
        }
        Store.Instance.DeleteEncrypted(matches[selected]);
        Console.WriteLine($"Deleted {matches[selected]}.");
        return;
    }
    PrintHelp();
    return;
}

if (args[0] == "--list" || args[0] == "-l")
{
    ListSavedHosts();
    return;
}

if ((args[0] is "--help" or "-h") || !ConnectToHost(args[0]))
{
    PrintHelp();
}

partial class Program
{
    private static int RunAliasCommand(string[] arguments)
    {
        try
        {
            switch (arguments)
            {
                case [] or ["list"]:
                    var aliases = AliasStore.Default.List();
                    if (aliases.Count == 0)
                    {
                        Console.WriteLine("No aliases.");
                    }
                    foreach (var (name, destination) in aliases)
                    {
                        Console.WriteLine($"{name} -> {destination}");
                    }
                    return 0;
                case ["set", var name, var destination]:
                    if (!AliasStore.IsValidName(name) || !SshDestination.TryParse(destination, out _))
                    {
                        Console.Error.WriteLine("Use an alias name of 1–64 ASCII letters, digits, hyphens or underscores, starting with a letter or digit, and a destination in [user@]host[:port] form.");
                        break;
                    }
                    AliasStore.Default.Set(name, destination);
                    Console.WriteLine($"Alias {name} -> {destination}");
                    return 0;
                case ["remove", var name] when AliasStore.IsValidName(name):
                    if (AliasStore.Default.Remove(name))
                    {
                        Console.WriteLine($"Removed alias {name}.");
                        return 0;
                    }
                    Console.Error.WriteLine($"No alias named {name}.");
                    break;
                default:
                    PrintHelp();
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not manage aliases: {ex.Message}");
        }
        return 1;
    }

    private static void RunHomeMenu()
    {
        while (true)
        {
            UI.WriteTitle("ClemSSH");
            switch (UI.Select("Use ↑/↓ and Enter to select, or Esc to exit.", ["Connect to a host", "Saved hosts", "Aliases", "Exit"]))
            {
                case 0:
                    string destination = UI.Input("Alias or [user@]host[:port] (blank to go back): ").Trim();
                    if (destination.Length > 0 && !ConnectToHost(destination))
                    {
                        Console.WriteLine("Invalid address. Enter [user@]host[:port] with a port from 1 to 65535.");
                    }
                    break;
                case 1:
                    ListSavedHosts();
                    break;
                case 2:
                    BrowseAliases();
                    break;
                default:
                    return;
            }
            Console.WriteLine();
        }
    }

    private static void BrowseAliases()
    {
        try
        {
            var aliases = AliasStore.Default.List();
            if (aliases.Count == 0)
            {
                Console.WriteLine("No aliases. Create one with: clemssh --alias set <name> [user@]host[:port]");
                return;
            }
            int selected = UI.Select("Aliases:", aliases.Select(alias => $"{alias.Key} -> {alias.Value}").ToArray());
            if (selected != -1)
            {
                ConnectToDestination(aliases[selected].Value);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not list aliases: {ex.Message}");
        }
    }

    private static void ListSavedHosts()
    {
        var keys = Store.Instance.Keys;
        if (keys.Count == 0)
        {
            Console.WriteLine("No saved hosts.");
            return;
        }
        NavigateMatches("All saved hosts:", [..keys]);
    }

    private static bool ConnectToHost(string destination)
    {
        try
        {
            destination = AliasStore.Default.Resolve(destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not resolve alias: {ex.Message}");
            return true;
        }
        return ConnectToDestination(destination);
    }

    private static bool ConnectToDestination(string destination)
    {
        if (!SshDestination.TryParse(destination, out var target))
        {
            return false;
        }

        var (host, user, port) = target;
        var matches = KeySearcher.FindMatches(target, Store.Instance.Keys);

        if (matches.Count == 0)
        {
            if (port == -1)
            {
                Console.WriteLine("No port, defaulting to 22");
                port = 22;
            }
            if (user == null)
            {
                Console.WriteLine("No user, defaulting to current user");
                user = Environment.UserName;
            }
            SSHClient.Open(host, port, user, null, savePassword: true);
            return true;
        }

        int selected = matches.Count == 1 ? 0 : UI.Select("Multiple matches found. Select one:", matches);
        if (selected == -1)
        {
            Console.WriteLine("Aborted.");
        }
        else
        {
            ConnectToSavedHost(matches[selected]);
        }
        return true;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  clemssh - Open the interactive menu");
        Console.WriteLine("  clemssh <alias> | [<user>@]<host>[:<port>] - Connect to a host");
        Console.WriteLine("  clemssh --alias [list] - List aliases");
        Console.WriteLine("  clemssh --alias set <name> [<user>@]<host>[:<port>] - Create or update an alias");
        Console.WriteLine("  clemssh --alias remove <name> - Remove an alias");
        Console.WriteLine("  clemssh (--list | -l) - List & manage saved hosts");
        Console.WriteLine("  clemssh (--search | -s) [<user>@]<host>[:<port>] - List & manage saved hosts by search");
        Console.WriteLine("  clemssh (--delete | -d) [<user>@]<host>[:<port>] - Delete a saved host");
    }
    private static void NavigateMatches(string title, List<string> matches)
    {
        while (matches.Count > 0)
        {
            int selected = UI.Select(title, matches);
            if (selected == -1)
            {
                return;
            }
            string selectedKey = matches[selected];
            while (true)
            {
                int action = UI.Select(selectedKey, ["Connect", "Delete", "Back"]);
                if (action == 0)
                {
                    ConnectToSavedHost(selectedKey);
                    return;
                }
                if (action != 1)
                {
                    break;
                }
                if (UI.Select($"Are you sure you want to delete {selectedKey}?", ["Yes", "No"], defaultIndex: 1) == 0)
                {
                    Store.Instance.DeleteEncrypted(selectedKey);
                    Console.WriteLine($"Deleted {selectedKey}");
                    matches.RemoveAt(selected);
                    break;
                }
            }
        }
    }

    private static void ConnectToSavedHost(string selectedKey)
    {
        if (!SshDestination.TryParse(selectedKey, out var saved) || saved.User is null || saved.Port == -1)
        {
            Console.Error.WriteLine($"Invalid saved destination: {selectedKey}");
            return;
        }
        if (!Store.Instance.LoadEncrypted(selectedKey, out string? pass))
        {
            Crasher.Crash($"Password for {selectedKey} not found in store.");
            return;
        }
        SSHClient.Open(saved.Host, saved.Port, saved.User, pass!, savePassword: false);
    }
}
