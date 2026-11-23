
namespace ClemSSH.Lib;

public static class KeySearcher
{
    public static bool Search(string host, string? user, int port, out List<string> matches)
    {
        matches = FindMatches(new SshDestination(host, user, port), Store.Instance.Keys);
        return matches.Count > 0;
    }

    public static List<string> FindMatches(SshDestination destination, IEnumerable<string> keys)
    {
        List<string> options = new List<string>();
        foreach (string key in keys)
        {
            if (SshDestination.TryParse(key, out var saved) && saved.User is not null && saved.Port != -1 &&
                string.Equals(saved.Host, destination.Host, StringComparison.OrdinalIgnoreCase) &&
                (destination.User is null || saved.User == destination.User) &&
                (destination.Port == -1 || saved.Port == destination.Port))
            {
                options.Add(key);
            }
        }
        return options;
    }
}
