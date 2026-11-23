using System.Text.RegularExpressions;

namespace ClemSSH.Lib;

public sealed partial class AliasStore(string directory)
{
    internal static AliasStore Default { get; } = new(Path.Combine(ConfigDirectory.GetPath(), "aliases"));

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_-]{0,63}\z")]
    private static partial Regex NameRegex();

    public static bool IsValidName(string name) => NameRegex().IsMatch(name);

    private string GetPath(string name)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException("Invalid alias name.", nameof(name));
        }
        return Path.Combine(directory, $"host-{name.ToLowerInvariant()}.txt");
    }

    // Alias targets are literal destinations; lookups never follow alias chains.
    public string Resolve(string destination) =>
        IsValidName(destination) ? Read(destination) ?? destination : destination;

    public IReadOnlyList<KeyValuePair<string, string>> List()
    {
        string[] paths;
        try
        {
            paths = Directory.GetFiles(directory, "host-*.txt");
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }

        var aliases = new List<KeyValuePair<string, string>>();
        foreach (string path in paths.Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(path)["host-".Length..];
            if (IsValidName(name) && Read(name) is string destination)
            {
                aliases.Add(new(name, destination));
            }
        }
        return aliases;
    }

    private string? Read(string name)
    {
        try
        {
            string destination = File.ReadAllText(GetPath(name)).Trim();
            if (!SshDestination.TryParse(destination, out _))
            {
                throw new IOException($"Alias '{name}' has an invalid destination.");
            }
            return destination;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public void Set(string name, string destination)
    {
        string path = GetPath(name);
        if (!SshDestination.TryParse(destination, out _))
        {
            throw new ArgumentException("Invalid SSH destination.", nameof(destination));
        }
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, destination + Environment.NewLine);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public bool Remove(string name)
    {
        string path = GetPath(name);
        try
        {
            File.GetAttributes(path);
            File.Delete(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }
}
