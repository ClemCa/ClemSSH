using ClemSSH.Lib;
using System.Security.Cryptography;
using Xunit;

namespace ClemSSH.Tests;

/// <summary>
/// Tests for the OS encryption layer (DPAPI on Windows, AES-GCM elsewhere)
/// and the PIN-wrapped key encryption utility.
/// </summary>
public class EncryptionTests
{
    [Fact]
    public void Encrypt_Decrypt_RoundTrips()
    {
        const string plaintext = "my secret password";

        string ciphertext = OS.Encrypt(plaintext);
        string decrypted = OS.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_SameInput_ProducesDifferentCiphertext()
    {
        const string plaintext = "secret";

        string a = OS.Encrypt(plaintext);
        string b = OS.Encrypt(plaintext);

        // DPAPI also adds a random salt; AES-GCM uses a random nonce.
        // Either way, two encryptions of the same plaintext must not be identical.
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Decrypt_InvalidBase64_ThrowsCryptographicException()
    {
        Assert.Throws<CryptographicException>(() => OS.Decrypt("not-valid-base64!!!"));
    }

    [Fact]
    public void Decrypt_GarbageBytes_DoesNotReturnPlaintext()
    {
        string garbage = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        Assert.ThrowsAny<CryptographicException>(() => OS.Decrypt(garbage));
    }

    [Fact]
    public void Encrypt_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OS.Encrypt(null!));
    }

    [Fact]
    public void Pin_Generate_IsEightDigits()
    {
        string pin = Pin.Generate();

        Assert.Equal(8, pin.Length);
        Assert.True(pin.All(char.IsDigit));
    }

    [Fact]
    public void Pin_Encrypt_Decrypt_RoundTrips()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        const string pin = "12345678";

        byte[] wrapped = Pin.Encrypt(key, pin);
        byte[] unwrapped = Pin.Decrypt(wrapped, pin);

        Assert.Equal(key, unwrapped);
    }

    [Fact]
    public void Pin_WrongPin_ThrowsCryptographicException()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);

        byte[] wrapped = Pin.Encrypt(key, "12345678");

        Assert.ThrowsAny<CryptographicException>(() => Pin.Decrypt(wrapped, "87654321"));
    }

    [Fact]
    public void Pin_Encrypt_ProducesDistinctCiphertext()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);

        byte[] a = Pin.Encrypt(key, "12345678");
        byte[] b = Pin.Encrypt(key, "12345678");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Pin_Decrypt_TruncatedInput_Throws()
    {
        byte[] wrapped = Pin.Encrypt(RandomNumberGenerator.GetBytes(32), "12345678");
        byte[] truncated = wrapped[..10];

        Assert.Throws<CryptographicException>(() => Pin.Decrypt(truncated, "12345678"));
    }
}
