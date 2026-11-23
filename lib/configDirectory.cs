namespace ClemSSH.Lib;

internal static class ConfigDirectory
{
    public static string GetPath(string? root = null)
    {
        if (root is null)
        {
            if (OperatingSystem.IsWindows())
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
            else
            {
                root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrEmpty(root))
                {
                    root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                }
            }
        }
        return Path.Combine(root, "clemssh");
    }
}
