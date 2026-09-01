using System.Security.Cryptography;

namespace ClemSSH.Lib;

public static class Pin
{
    // extra entropy
    private const int KdfIterations = 300_000; // PBKDF2 iterations
    private const int SaltSize = 16;
    private const int KeySize = 32; // AES-256
    private const int NonceSize = 12;  // GCM nonce
    private const int TagSize = 16;    // GCM tag
    private const int PinLength = 8;   // digits

    public static string Generate()
    {
        // 0-padded digits
        int value = RandomNumberGenerator.GetInt32(0, 1_0000_0000);
        return value.ToString($"D{PinLength}")[^PinLength..];
    }

    public static byte[] Encrypt(byte[] key, string pin)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] kek = DeriveKey(pin, salt);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] ciphertext = new byte[key.Length];
        byte[] tag = new byte[TagSize];

        using var aes = new AesGcm(kek, TagSize);
        aes.Encrypt(nonce, key, ciphertext, tag);

        // [salt, nonce, tag, ciphertext]
        byte[] result = new byte[SaltSize + NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(nonce, 0, result, SaltSize, NonceSize);
        Buffer.BlockCopy(tag, 0, result, SaltSize + NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, SaltSize + NonceSize + TagSize, ciphertext.Length);
        return result;
    }

    public static byte[] Decrypt(byte[] wrapped, string pin)
    {
        if (wrapped.Length < SaltSize + NonceSize + TagSize)
        {
            throw new CryptographicException("Invalid wrapped key");
        }

        byte[] salt = wrapped.AsSpan(0, SaltSize).ToArray();
        byte[] nonce = wrapped.AsSpan(SaltSize, NonceSize).ToArray();
        byte[] tag = wrapped.AsSpan(SaltSize + NonceSize, TagSize).ToArray();
        byte[] ciphertext = wrapped.AsSpan(SaltSize + NonceSize + TagSize).ToArray();

        byte[] kek = DeriveKey(pin, salt);
        byte[] plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(kek, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    private static byte[] DeriveKey(string pin, byte[] salt)
    {
        // PBKDF2-HMAC-SHA256 to make guesses more expensive
        // does not make it more secure but more painful
        return Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            KdfIterations,
            HashAlgorithmName.SHA256,
            KeySize);
    }
}
