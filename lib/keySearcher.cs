
namespace ClemSSH.Lib;

public static class KeySearcher
{
    public static bool Search(string host, string? user, int port, out List<string> matches)
    {
        List<string> options = new List<string>();
        foreach (string key in Store.Instance.Keys)
        {
            string[] parts = key.Split(':');
            if (parts.Length != 3) continue;
            string kHost = parts[0];
            string kUser = parts[1];
            int kPort = int.Parse(parts[2]);

            if (kHost == host && (user == null || kUser == user) && (port == -1 || kPort == port))
            {
                options.Add(key);
            }
        }
        matches = options;
        return options.Count > 0;
    }
}