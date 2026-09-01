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
        int top = Console.CursorTop + 1;

        Console.WriteLine(prompt);
        Console.CursorVisible = false;

        try
        {
            RenderOptions(options, current, top);
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        current = (current - 1 + options.Count) % options.Count;
                        RenderOptions(options, current, top);
                        break;
                    case ConsoleKey.DownArrow:
                        current = (current + 1) % options.Count;
                        RenderOptions(options, current, top);
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

    private static void RenderOptions(IReadOnlyList<string> options, int current, int top)
    {
        lock (ConsoleLock)
        {
            int cursorTop = Console.CursorTop;
            for (int i = 0; i < options.Count; i++)
            {
                Console.SetCursorPosition(0, top + i);
                bool selected = i == current;
                var prev = Console.BackgroundColor;

                if (selected)
                {
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.BackgroundColor = ConsoleColor.White;
                }
                else
                {
                    Console.ResetColor();
                }

                Console.Write($"  {(selected ? "> " : "  ")}{i + 1}. {options[i]}".PadRight(Console.WindowWidth - 1));
                Console.ResetColor();
            }
            Console.SetCursorPosition(0, top + options.Count);
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
