// Handle OS-specific functionality.
//
// Windows -> DPAPI via System.Security.Cryptography.ProtectedData
// Others -> AES-256-GCM with a key file

using System.Security.Cryptography;
using System.Text;

namespace ClemSSH.Lib;

public static class OS
{
    private const int KeySize = 32;   // AES-256
    private const int NonceSize = 12; // GCM recommended nonce size
    private const int TagSize = 16;   // GCM authentication tag size
    private static readonly byte[] AppEntropy = Encoding.UTF8.GetBytes("clemssh-store");
    public static string Encrypt(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        byte[] plaintext = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(EncryptBytes(plaintext));
    }

    public static string Decrypt(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        byte[] plaintext;
        try {
            plaintext = DecryptBytes(Convert.FromBase64String(input));
        } catch (FormatException) {
            throw new CryptographicException("Invalid input format");
        } catch (CryptographicException) {
            throw;
        }
        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] EncryptBytes(byte[] data)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Protect(data, optionalEntropy: AppEntropy, DataProtectionScope.CurrentUser);
        }
        byte[] key = Store.Instance.GetSafeKey();
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] ciphertext = new byte[data.Length];
        byte[] tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, data, ciphertext, tag);

        // [nonce, tag, ciphertext]
        byte[] result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);
        return result;
    }

    private static byte[] DecryptBytes(byte[] data)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Unprotect(data, optionalEntropy: AppEntropy, DataProtectionScope.CurrentUser);
        }

        if (data.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Invalid data");
        }

        byte[] key = Store.Instance.GetSafeKey();
        byte[] nonce = data.AsSpan(0, NonceSize).ToArray();
        byte[] tag = data.AsSpan(NonceSize, TagSize).ToArray();
        byte[] ciphertext = data.AsSpan(NonceSize + TagSize).ToArray();

        byte[] plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}