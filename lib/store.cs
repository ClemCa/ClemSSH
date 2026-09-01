

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClemSSH.Lib;

public class Store
{
    public static Store Instance { get; } = new Store();
    private string filePath = null!;
    private string keyFilePath = null!;
    private Dictionary<string, int> linesCache = [];
    private int keyStart;
    private const int AESKeySize = 32; // AES-256
    private FileStream? lockStream;

    public ICollection<string> Keys => linesCache.Keys;

    public Store()
    {
        Initialize(null);
    }

    // test-only: point the store at an isolated directory rather than the real config location
    public Store(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Directory must not be empty.", nameof(directory));
        }
        Initialize(directory);
    }

    private void Initialize(string? directory)
    {
        if (OperatingSystem.IsWindows())
        {
            string appData = directory ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            filePath = Path.Combine(appData, "clemssh", "store.json");
            keyFilePath = ""; // not used on Windows
        }
        else
        {
            string? configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(configHome))
            {
                configHome = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            }
            filePath = Path.Combine(configHome, "clemssh", "store.json");
            keyFilePath = Path.Combine(configHome, "clemssh", "secret.key");
        }
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            Directory.CreateDirectory(dir!);
            if (!File.Exists(filePath))
            {
                using var writer = new BinaryWriter(File.OpenWrite(filePath));
                writer.Write(0);
            }
        }
        catch (Exception ex)
        {
            Crasher.Crash($"Failed to initialize store: {ex.Message}");
        }
        PopulateLineCache();
    }

    public bool LoadEncrypted(string key, out string? value)
    {
        bool alreadyLocked = lockStream != null;
        if (!alreadyLocked)
        {
            LockFile();
        }
        try
        {
            if (!File.Exists(filePath))
            {
                value = null;
                return false;
            }
            if (!linesCache.TryGetValue(key, out int lineNumber))
            {
                value = null;
                return false;
            }

            GetLine(lineNumber, out string? stored);
            if (stored == null)
            {
                value = null;
                return false;
            }
            // remove padding
            string ciphertext = stored.TrimEnd();
            string plainText;
            try {
                plainText = OS.Decrypt(ciphertext);
            }
            catch (Exception)
            {
                value = null;
                return false;
            }
            // verify we have the right key (anti-tamper)

            if (!plainText.StartsWith(key, StringComparison.Ordinal))
            {
                value = null;
                return false;
            }
            value = plainText[key.Length..];
            return true;
        }
        finally
        {
            if (!alreadyLocked)
            {
                UnlockFile();
            }
        }
    }

    public void SaveEncrypted(string key, string value)
    {
        bool alreadyLocked = lockStream != null;
        if (!alreadyLocked)
        {
            LockFile();
        }
        try
        {
            // refresh cache in case it was written by someone else (another instance)
            PopulateLineCache();
            // prefix the key to prevent key-swapping attacks
            SetKey(key, OS.Encrypt(key + value)); // encrypt pre-padding
            SaveLineCache();
            // flush to avoid losing data on sudden power loss or system crash
            lockStream!.Flush(flushToDisk: true);
        }
        finally
        {
            if (!alreadyLocked)
            {
                UnlockFile();
            }
        }
    }

    public void DeleteEncrypted(string key)
    {
        bool alreadyLocked = lockStream != null;
        if (!alreadyLocked)
        {
            LockFile();
        }
        try
        {
            // refresh cache in case it was written by someone else (another instance)
            PopulateLineCache();
            if (linesCache.TryGetValue(key, out int lineNumber))
            {
                linesCache.Remove(key);
                SetLine(key, lineNumber, ""); // clear the line
                SaveLineCache();
                lockStream!.Flush(flushToDisk: true);
            }
        }
        finally
        {
            if (!alreadyLocked)
            {
                UnlockFile();
            }
        }
    }

    // OS-specific keyring access for non-Windows platforms
    private const string KeyService = "clemssh";
    private const string KeyAccount = "store-master-key";

    public byte[] GetSafeKey()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("GetSafeKey should not be called on Windows");
        }

        string? tool, readArgs, writeArgs;
        if (OperatingSystem.IsMacOS())
        {
            tool = "security";
            readArgs = $"find-generic-password -s {KeyService} -a {KeyAccount} -w";
            writeArgs = $"add-generic-password -U -s {KeyService} -a {KeyAccount} -w";
        }
        else // Linux
        {
            tool = "secret-tool";
            readArgs = $"lookup service {KeyService} account {KeyAccount}";
            writeArgs = $"store --label={KeyService} service {KeyService} account {KeyAccount}";
        }

        // try keyring
        string? existing = RunKeyringTool(tool, readArgs, input: null);
        if (existing != null)
        {
            return Convert.FromBase64String(existing);
        }

        // no keyring
        byte[] key = RandomNumberGenerator.GetBytes(AESKeySize);
        string b64 = Convert.ToBase64String(key);

        // linux = stdin, macos = arg
        bool stored = OperatingSystem.IsLinux()
            ? RunKeyringTool(tool, writeArgs, b64) != null
            : RunKeyringTool(tool, $"{writeArgs} {b64}", input: null) != null;

        if (stored)
        {
            return key;
        }

        // fallback: PIN-protected key file.
        string? dir = Path.GetDirectoryName(keyFilePath);
        Directory.CreateDirectory(dir!);

        if (File.Exists(keyFilePath))
        {
            byte[] wrapped = File.ReadAllBytes(keyFilePath);
            for (int attempt = 0; ; attempt++)
            {
                Console.Write("Enter PIN to unlock the store: ");
                string? pinInput = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(pinInput))
                {
                    Crasher.Crash("A PIN is required to unlock the store.");
                }
                string pin = pinInput!;
                try
                {
                    return Pin.Decrypt(wrapped, pin);
                }
                catch (CryptographicException)
                {
                    if (attempt >= 2)
                    {
                        Crasher.Crash("Too many incorrect PIN attempts.");
                    }
                    Console.WriteLine("Incorrect PIN, try again.");
                }
            }
        }

        // generate pin
        string newPin = Pin.Generate();
        byte[] wrappedKey = Pin.Encrypt(key, newPin);
        File.WriteAllBytes(keyFilePath, wrappedKey);
        // restrict to the current user
        File.SetUnixFileMode(keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Console.WriteLine();
        Console.WriteLine("No keyring is available on this system.");
        Console.WriteLine($"  Generated PIN: {newPin}");
        Console.WriteLine("You will be asked for this PIN each time you need to access the store, please store it safely.");
        Console.WriteLine();
        return key;
    }
    // helper to run keyring commands
    private static string? RunKeyringTool(string tool, string args, string? input)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = tool,
                Arguments = args,
                RedirectStandardInput = input != null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null)
            {
                return null;
            }

            // read stdout/stderr asynchronously so the child never blocks on a full pipe buffer
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            if (input != null)
            {
                // write stdin concurrently, then close it (linux secret-tool takes the secret on stdin)
                p.StandardInput.Write(input);
                p.StandardInput.Close();
            }

            p.WaitForExit();
            stdoutTask.Wait();
            stderrTask.Wait();

            return p.ExitCode == 0 ? stdoutTask.Result.Trim() : null;
        }
        catch (Exception)
        {
            // tool not installed
            return null;
        }
    }

    private void LockFile()
    {
        if (lockStream != null)
        {
            return; // already locked
        }
        lockStream = new FileStream(
            filePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.None);
    }

    private void UnlockFile()
    {
        lockStream?.Dispose();
        lockStream = null;
    }


    private void PopulateLineCache()
    {
        if (!File.Exists(filePath)) // we should have created it by now but in case
        {
            return;
        }

        bool ownsStream = lockStream == null;
        FileStream? fs = lockStream;
        fs ??= new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.None);
        try
        {
            fs.Seek(0, SeekOrigin.Begin);
            using var reader = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
            // read keys length
            int paddedSize = reader.ReadInt32();
            if (paddedSize == 0)
            {
                keyStart = SafePosition(reader.BaseStream.Position);
                return;
            }
            string keysJson = Encoding.UTF8.GetString(reader.ReadBytes(paddedSize)).TrimEnd('\0', ' '); // strip padding
            var keys = JsonSerializer.Deserialize<Dictionary<string, int>>(keysJson);
            if (keys != null)
            {
                linesCache = keys;
            }
            keyStart = SafePosition(reader.BaseStream.Position);
        }
        catch (JsonException)
        {
            //! panic instead of wiping accidentally
            Crasher.Crash("Failed to deserialize keys from store.json, the file may be corrupted.");
        }
        catch (EndOfStreamException)
        {
            // truncated header: keys table length promises more bytes than the file has
            Crasher.Crash("store.json is truncated or corrupted.");
        }
        finally
        {
            if (ownsStream)
            {
                fs.Dispose();
            }
        }
    }

    private void SaveLineCache()
    {
        if (lockStream == null)
        {
            Crasher.Crash("SaveLineCache called without a lock, this is a critical logic bug that could lead to data loss");
            return; // return not needed, but the compiler does not know
        }
        string keysJson = JsonSerializer.Serialize(linesCache);
        byte[] keysBytes = Encoding.UTF8.GetBytes(keysJson);
        int keysSize = keysBytes.Length;
        // 2kb chunks to avoid resizing too often, especially when files get large
        // if we weren't dealing with human scale and expected gigabytes of data, we could use a linked format
        int paddedSize = SafeLength((long)keysSize + 2047) / 2048 * 2048;
        // keyStart was set in populate just before, the lock was already active
        int previousPaddedSize = keyStart - sizeof(int);

        if (paddedSize == previousPaddedSize)
        {
            // overwrite in place
            using var writer = new BinaryWriter(lockStream, Encoding.UTF8, leaveOpen: true);
            lockStream.Seek(0, SeekOrigin.Begin);
            writer.Write(paddedSize);
            writer.Write(keysBytes);
            writer.Write(Encoding.UTF8.GetBytes(new string(' ', paddedSize - keysSize)));
        }
        else
        {
            byte[] padded = new byte[paddedSize];
            Array.Copy(keysBytes, padded, keysSize);
            // pad the remainder with spaces
            for (int i = keysSize; i < padded.Length; i++)
            {
                padded[i] = (byte)' ';
            }
            using (var writer = new BinaryWriter(lockStream, Encoding.UTF8, leaveOpen: true))
            {
                lockStream.Seek(0, SeekOrigin.Begin);
                writer.Write(paddedSize);
            }
            SlidingInsert(new Range(sizeof(int), keyStart), padded);
            keyStart = SafeLength((long)sizeof(int) + paddedSize);
        }
    }

    private void SetKey(string key, string encryptedValue)
    {
        if (linesCache.TryGetValue(key, out int lineNumber))
        {
            SetLine(key, lineNumber, encryptedValue);
            return;
        }
        // find a free line number
        HashSet<int> usedLineNumbers = [..linesCache.Values];
        for (int i = 0; i < int.MaxValue; i++)
        {
            if (!usedLineNumbers.Contains(i))
            {
                linesCache[key] = i;
                SetLine(key, i, encryptedValue);
                return;
            }
        }
        Crasher.Crash("Failed to find a free line number in the store, reached maximum storage capacity.");
    }

    private void GetLine(int lineNumber, out string outLine)
    {
        if (lockStream == null)
        {
            Crasher.Crash("SlidingInsert called without a lock, this is a critical logic bug that could lead to data loss");
            // not needed, but the compiler does not know
            outLine = "";
            return;
        }
        using var reader = new BinaryReader(lockStream, Encoding.UTF8, leaveOpen: true);
        reader.BaseStream.Seek(keyStart, SeekOrigin.Begin);

        int line = 0;
        // skip to the target line, counting raw 0x0A newline bytes
        while (line < lineNumber && reader.BaseStream.Position < reader.BaseStream.Length)
        {
            if (reader.ReadByte() == (byte)'\n')
            {
                line++;
            }
        }

        var raw = new List<byte>();
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            int b = reader.ReadByte();
            if (b == (byte)'\n')
            {
                break;
            }
            raw.Add((byte)b);
        }

        outLine = Encoding.UTF8.GetString([..raw]).TrimEnd();
    }

    private void SetLine(string key, int lineNumber, string encryptedValue)
    {
        if (lockStream == null)
        {
            Crasher.Crash("SetLine called without a lock, this is a critical logic bug that could lead to data loss");
            return; // return not needed, but the compiler does not know
        }
        byte[] valueBytes = Encoding.UTF8.GetBytes(encryptedValue);
        int newValueSize = valueBytes.Length;

        bool regionEmpty;
        bool lineFound;
        long lineStart = 0;
        int spaceAvailable = 0;
        int lineCount;

        using (var reader = new BinaryReader(lockStream, Encoding.UTF8, leaveOpen: true))
        {
            reader.BaseStream.Seek(keyStart, SeekOrigin.Begin);
            regionEmpty = reader.BaseStream.Position >= reader.BaseStream.Length;

            int line = 0;
            int totalNewlines = 0;

            // skip to the target line, counting newlines on the way
            while (line < lineNumber && reader.BaseStream.Position < reader.BaseStream.Length)
            {
                if (reader.ReadByte() == (byte)'\n')
                {
                    line++;
                    totalNewlines++;
                }
            }

            lineFound = line == lineNumber;
            if (lineFound)
            {
                // measure how much room this line has (up to the next newline, or EOF for the last line)
                lineStart = reader.BaseStream.Position;
                bool hasNewline = false;
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    if (reader.ReadByte() == (byte)'\n')
                    {
                        totalNewlines++;
                        hasNewline = true;
                        break;
                    }
                }
                // the newline itself isn't usable space
                spaceAvailable = SafeLength(reader.BaseStream.Position - lineStart - (hasNewline ? 1 : 0));
            }

            // count remaining newlines so we know how many lines exist
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                if (reader.ReadByte() == (byte)'\n')
                {
                    totalNewlines++;
                }
            }

            lineCount = regionEmpty ? 0 : totalNewlines + 1;
        }

        if (lineFound && newValueSize <= spaceAvailable)
        {
            // overwrite in place, padding with spaces if shorter (post-encryption padding)
            using var writer = new BinaryWriter(lockStream, Encoding.UTF8, leaveOpen: true);
            lockStream.Seek(lineStart, SeekOrigin.Begin);
            writer.Write(valueBytes);
            writer.Write(Encoding.UTF8.GetBytes(new string(' ', spaceAvailable - newValueSize)));
        }
        else
        {
            // append a new line at the end
            using var writer = new BinaryWriter(lockStream, Encoding.UTF8, leaveOpen: true);
            lockStream.Seek(0, SeekOrigin.End);
            if (!regionEmpty)
            {
                writer.Write(Encoding.UTF8.GetBytes("\n"));
            }
            writer.Write(valueBytes);
            linesCache[key] = lineCount;
        }
    }

    private void SlidingInsert(Range oldRange, byte[] newValue)
    {
        if (lockStream == null)
        {
            Crasher.Crash("SlidingInsert called without a lock, this is a critical logic bug that could lead to data loss");
            return; // return not needed, but the compiler does not know
        }
        // read the tail (everything after the replaced region)
        byte[] tail;
        using (var reader = new BinaryReader(lockStream, Encoding.UTF8, leaveOpen: true))
        {
            lockStream.Seek(oldRange.End.Value, SeekOrigin.Begin);
            long tailLength = lockStream.Length - oldRange.End.Value;
            tail = reader.ReadBytes(SafeLength(tailLength));
        }

        using var writer = new BinaryWriter(lockStream, Encoding.UTF8, leaveOpen: true);
        lockStream.Seek(oldRange.Start.Value, SeekOrigin.Begin);
        writer.Write(newValue);
        writer.Write(tail);
        lockStream.SetLength(oldRange.Start.Value + newValue.Length + tail.Length);
    }

    private static int SafePosition(long position)
    {
        if (position < 0 || position > int.MaxValue)
        {
            Crasher.Crash("Store file exceeds 2GB, which is not supported.");
        }
        return (int)position;
    }

    private static int SafeLength(long length)
    {
        if (length < 0 || length > int.MaxValue)
        {
            Crasher.Crash("Store file exceeds 2GB, which is not supported.");
        }
        return (int)length;
    }
}