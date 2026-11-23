using System.Text;

namespace ClemSSH.Client;

public static class UI
{
    private static readonly object ConsoleLock = new();

    public static int Select(string prompt, IReadOnlyList<string> options, int defaultIndex = 0, string? description = null)
    {
        if (options.Count == 0)
        {
            throw new ArgumentException("No options to select from.", nameof(options));
        }

        int current = Math.Clamp(defaultIndex, 0, options.Count - 1);
        if (Console.CursorLeft != 0)
        {
            Console.WriteLine();
        }
        // Reserve rows by writing them so the terminal can scroll at the bottom.
        if (Console.WindowHeight < 4)
        {
            description = null;
        }
        int headerRows = description is null ? 1 : 2;
        int visibleCount = Math.Min(options.Count, Math.Max(1, Console.WindowHeight - headerRows - 1));
        int rowCount = visibleCount + headerRows;
        for (int i = 0; i < rowCount; i++)
        {
            Console.WriteLine();
        }
        int top = Math.Max(0, Console.CursorTop - rowCount);
        Console.CursorVisible = false;

        try
        {
            WriteMenuRow(top, prompt);
            if (description is not null)
            {
                WriteMenuRow(top + 1, description);
            }
            int optionsTop = top + headerRows;
            RenderOptions(options, current, optionsTop, visibleCount);
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        current = (current - 1 + options.Count) % options.Count;
                        RenderOptions(options, current, optionsTop, visibleCount);
                        break;
                    case ConsoleKey.DownArrow:
                        current = (current + 1) % options.Count;
                        RenderOptions(options, current, optionsTop, visibleCount);
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
            try
            {
                Console.ResetColor();
                for (int row = 0; row < rowCount; row++)
                {
                    WriteMenuRow(top + row, "");
                }
                Console.SetCursorPosition(0, Math.Min(top, Math.Max(0, Console.BufferHeight - 1)));
            }
            finally
            {
                Console.CursorVisible = true;
            }
        }
    }

    private static void WriteMenuRow(int row, string text)
    {
        if (row >= Console.BufferHeight)
        {
            return;
        }
        Console.SetCursorPosition(0, row);
        int width = Math.Max(0, Console.WindowWidth - 1);
        Console.Write(text.Length > width ? text[..width] : text.PadRight(width));
    }

    private static void RenderOptions(IReadOnlyList<string> options, int current, int top, int visibleCount)
    {
        lock (ConsoleLock)
        {
            int first = Math.Min(current / visibleCount * visibleCount, options.Count - visibleCount);
            for (int row = 0; row < visibleCount; row++)
            {
                int i = first + row;
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

                WriteMenuRow(top + row, $"  {(selected ? "> " : "  ")}{i + 1}. {options[i]}");
                Console.ResetColor();
            }
        }
    }

    public static string Input(string prompt, bool transient = false)
    {
        lock (ConsoleLock)
        {
            int start = 0;
            if (transient)
            {
                if (Console.CursorLeft != 0)
                {
                    Console.WriteLine();
                }
                start = Console.CursorTop;
                int width = Math.Max(0, Console.WindowWidth - 1);
                Console.Write(prompt.Length > width ? prompt[..width] : prompt);
            }
            else
            {
                Console.Write(prompt);
            }
            string? input = Console.ReadLine();
            if (transient)
            {
                int end = Console.CursorTop;
                // Enter may scroll the buffer when the prompt is on its last row.
                if (input is not null && end == Console.BufferHeight - 1)
                {
                    int width = Math.Max(1, Console.WindowWidth);
                    int cells = Math.Min(prompt.Length, width - 1) + input.Length;
                    int rows = Math.Max(1, (cells + width - 1) / width);
                    start = Math.Min(start, end - rows);
                }
                start = Math.Max(0, Math.Min(start, end));
                for (int row = start; row <= end; row++)
                {
                    WriteMenuRow(row, "");
                }
                Console.SetCursorPosition(0, start);
            }
            return input ?? string.Empty;
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
