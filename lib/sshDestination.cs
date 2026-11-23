using System.Text.RegularExpressions;

namespace ClemSSH.Lib;

public readonly partial record struct SshDestination(string Host, string? User, int Port)
{
    [GeneratedRegex(@"\A(?:(?<user>[^\s\p{Cc}@:/\\]+)@)?(?<host>[^\s\p{Cc}@:/\\]+)(?::(?<port>[0-9]+))?\z")]
    private static partial Regex DestinationRegex();

    public static bool TryParse(string value, out SshDestination destination)
    {
        destination = default;
        var match = DestinationRegex().Match(value);
        int port = -1;
        if (!match.Success || match.Groups["host"].Value.StartsWith('-') ||
            (match.Groups["port"].Success &&
             (!int.TryParse(match.Groups["port"].Value, out port) || port is < 1 or > 65535)))
        {
            return false;
        }

        destination = new(match.Groups["host"].Value,
            match.Groups["user"].Success ? match.Groups["user"].Value : null, port);
        return true;
    }
}
