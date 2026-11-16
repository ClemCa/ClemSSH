using ClemSSH.Client;
using System.Text;
using Xunit;

public class InteractiveShellTests
{
    [Theory]
    [InlineData(ConsoleKey.UpArrow, '\0', "\x1b[A")]
    [InlineData(ConsoleKey.Delete, '\0', "\x1b[3~")]
    [InlineData(ConsoleKey.Backspace, '\b', "\x7f")]
    [InlineData(ConsoleKey.Enter, '\r', "\r")]
    [InlineData(ConsoleKey.C, '\x03', "\x03")]
    [InlineData(ConsoleKey.A, 'a', "a")]
    public void EncodesTerminalKeys(ConsoleKey key, char character, string expected)
    {
        var input = new ConsoleKeyInfo(character, key, false, false, false);
        Assert.Equal(expected, Encoding.UTF8.GetString(InteractiveShell.EncodeKey(input)));
    }
}
