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
    private const string AliasNameHint = "Alias names use 1–64 ASCII letters, digits, hyphens or underscores, starting with a letter or digit.";
    private const string InvalidDestination = "Invalid address. Enter [user@]host[:port] with a port from 1 to 65535.";

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
                    if (!AliasStore.IsValidName(name))
                    {
                        Console.Error.WriteLine(AliasNameHint);
                        break;
                    }
                    if (!SshDestination.TryParse(destination, out _))
                    {
                        Console.Error.WriteLine(InvalidDestination);
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
            switch (UI.Select("ClemSSH — ↑/↓ and Enter to select, Esc to exit", ["Connect to a host", "Saved hosts", "Aliases", "Exit"]))
            {
                case 0:
                    PickConnection();
                    break;
                case 1:
                    ListSavedHosts();
                    break;
                case 2:
                    ManageAliases();
                    break;
                default:
                    return;
            }
        }
    }

    private static string[] SavedDestinations() => Store.Instance.Keys
        .Where(key => SshDestination.TryParse(key, out var target) && target.User is not null && target.Port != -1)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static void PickConnection()
    {
        IReadOnlyList<KeyValuePair<string, string>> aliases;
        try
        {
            aliases = AliasStore.Default.List();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not list aliases: {ex.Message}");
            aliases = [];
        }

        var savedHosts = SavedDestinations();
        if (aliases.Count > 0 || savedHosts.Length > 0)
        {
            string[] options = [..aliases.Select(alias => $"{alias.Key} -> {alias.Value}"), ..savedHosts, "Enter address manually", "Back"];
            int selected = UI.Select("Connect to a host", options);
            if (selected == -1 || selected == options.Length - 1)
            {
                return;
            }
            if (selected < aliases.Count)
            {
                ConnectToDestination(aliases[selected].Value);
                return;
            }
            int savedIndex = selected - aliases.Count;
            if (savedIndex < savedHosts.Length)
            {
                ConnectToSavedHost(savedHosts[savedIndex]);
                return;
            }
        }

        string destination = UI.Input("Alias or [user@]host[:port] (blank to go back): ", transient: true).Trim();
        if (destination.Length > 0 && !ConnectToHost(destination))
        {
            Console.WriteLine(InvalidDestination);
        }
    }

    private static string PickAliasDestination(string name)
    {
        var savedHosts = SavedDestinations();
        if (savedHosts.Length > 0)
        {
            string[] options = [..savedHosts, "Enter address manually", "Cancel"];
            int selected = UI.Select("Choose a saved host", options, description: $"Destination for alias '{name}'");
            if (selected == -1 || selected == options.Length - 1)
            {
                return "";
            }
            if (selected < savedHosts.Length)
            {
                return savedHosts[selected];
            }
        }

        return ReadAliasDestination();
    }

    private static string ReadAliasDestination() =>
        UI.Input("Destination [user@]host[:port] (blank to cancel): ", transient: true).Trim();

    private static void ManageAliases()
    {
        while (true)
        {
            try
            {
                var aliases = AliasStore.Default.List();
                string[] options = ["Add alias", ..aliases.Select(alias => $"{alias.Key} -> {alias.Value}"), "Back"];
                int selected = UI.Select("Aliases", options);
                if (selected == -1 || selected == options.Length - 1)
                {
                    return;
                }
                if (selected == 0)
                {
                    string name = UI.Input("Alias name (blank to cancel): ", transient: true).Trim();
                    if (name.Length > 0)
                    {
                        SaveAliasFromMenu(name, isNew: true);
                    }
                    continue;
                }

                var (alias, destination) = aliases[selected - 1];
                switch (UI.Select($"{alias} -> {destination}", ["Connect", "Edit destination", "Remove alias", "Back"]))
                {
                    case 0:
                        ConnectToDestination(destination);
                        return;
                    case 1:
                        SaveAliasFromMenu(alias, isNew: false);
                        break;
                    case 2:
                        if (UI.Select($"Remove alias '{alias}'?", ["No", "Yes"]) == 1)
                        {
                            Console.WriteLine(AliasStore.Default.Remove(alias)
                                ? $"Removed alias {alias}."
                                : $"No alias named {alias}.");
                        }
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not manage aliases: {ex.Message}");
                return;
            }
        }
    }

    private static void SaveAliasFromMenu(string name, bool isNew)
    {
        if (!AliasStore.IsValidName(name))
        {
            Console.Error.WriteLine(AliasNameHint);
            return;
        }

        string destination = isNew ? PickAliasDestination(name) : ReadAliasDestination();
        if (destination.Length == 0)
        {
            return;
        }
        if (!SshDestination.TryParse(destination, out _))
        {
            Console.Error.WriteLine(InvalidDestination);
            return;
        }

        if (isNew)
        {
            if (!AliasStore.Default.TryAdd(name, destination))
            {
                Console.Error.WriteLine($"Alias '{name}' already exists. Select it from the list to edit it.");
                return;
            }
        }
        else
        {
            AliasStore.Default.Set(name, destination);
        }
        Console.WriteLine($"Alias {name} -> {destination}");
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
