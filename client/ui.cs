using System.Text;

namespace ClemSSH.Client;

public static class UI
{
    private static readonly object ConsoleLock = new();

    public static void WriteTitle(string text)
    {
        lock (ConsoleLock)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(text);
            Console.ForegroundColor = prev;
        }
    }

    public static int Select(string prompt, IReadOnlyList<string> options, int defaultIndex = 0)
    {
        if (options.Count == 0)
        {
            throw new ArgumentException("No options to select from.", nameof(options));
        }

        int current = Math.Clamp(defaultIndex, 0, options.Count - 1);
        Console.WriteLine(prompt);
        // Reserve rows by writing them so the terminal can scroll at the bottom.
        int visibleCount = Math.Min(options.Count, Math.Max(1, Console.WindowHeight - 2));
        for (int i = 0; i < visibleCount; i++)
        {
            Console.WriteLine();
        }
        int bottom = Console.CursorTop;
        Console.CursorVisible = false;

        try
        {
            RenderOptions(options, current, bottom, visibleCount);
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        current = (current - 1 + options.Count) % options.Count;
                        RenderOptions(options, current, bottom, visibleCount);
                        break;
                    case ConsoleKey.DownArrow:
                        current = (current + 1) % options.Count;
                        RenderOptions(options, current, bottom, visibleCount);
                        break;
                    case ConsoleKey.Enter:
                        return current;
                    case ConsoleKey.Escape:
                        return -1;
                    default:
                        // number for options
                        if (key.KeyChar is >= '1' and <= '9')
                        {
                            int idx = key.KeyChar - '1';
                            if (idx < options.Count)
                            {
                                return idx;
                            }
                        }
                        break;
                }
            }
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    private static void RenderOptions(IReadOnlyList<string> options, int current, int bottom, int visibleCount)
    {
        lock (ConsoleLock)
        {
            int first = Math.Min(current / visibleCount * visibleCount, options.Count - visibleCount);
            int width = Math.Max(0, Console.WindowWidth - 1);
            for (int row = 0; row < visibleCount; row++)
            {
                int i = first + row;
                Console.SetCursorPosition(0, bottom - visibleCount + row);
                bool selected = i == current;

                if (selected)
                {
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.BackgroundColor = ConsoleColor.White;
                }
                else
                {
                    Console.ResetColor();
                }

                string line = $"  {(selected ? "> " : "  ")}{i + 1}. {options[i]}";
                Console.Write(line.Length > width ? line[..width] : line.PadRight(width));
                Console.ResetColor();
            }
            Console.SetCursorPosition(0, bottom);
        }
    }

    public static string Input(string prompt)
    {
        lock (ConsoleLock)
        {
            Console.Write(prompt);
            return Console.ReadLine() ?? string.Empty;
        }
    }

    public static string Input(string prompt, Func<string, bool> validator, string errorMessage)
    {
        while (true)
        {
            string value = Input(prompt);
            if (validator(value))
            {
                return value;
            }
            Console.Error.WriteLine(errorMessage);
        }
    }

    public static string Password(string prompt)
    {
        lock (ConsoleLock)
        {
            Console.Write(prompt);
            var sb = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return sb.ToString();
                }
                if (key.Key == ConsoleKey.Escape)
                {
                    Console.WriteLine();
                    return string.Empty;
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0)
                    {
                        sb.Length--;
                        Console.Write("\b \b");
                    }
                    continue;
                }
                if (char.IsControl(key.KeyChar))
                {
                    continue;
                }
                sb.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }

    public static string PasswordSilent(string prompt)
    {
        lock (ConsoleLock)
        {
            Console.Write(prompt);
            var sb = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return sb.ToString();
                }
                if (key.Key == ConsoleKey.Escape)
                {
                    Console.WriteLine();
                    return string.Empty;
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0) sb.Length--;
                    continue;
                }
                if (char.IsControl(key.KeyChar)) continue;
                sb.Append(key.KeyChar);
            }
        }
    }

    public static bool Confirm(string prompt, bool defaultYes = true)
    {
        string suffix = defaultYes ? "[Y/n]" : "[y/N]";
        while (true)
        {
            string answer = Input($"{prompt} {suffix} ").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(answer))
            {
                return defaultYes;
            }
            if (answer is "y" or "yes")
            {
                return true;
            }
            if (answer is "n" or "no")
            {
                return false;
            }
        }
    }
}
