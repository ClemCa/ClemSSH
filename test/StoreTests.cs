using ClemSSH.Lib;
using Xunit;

namespace ClemSSH.Tests;

/// <summary>
/// Tests for the encrypted key/value store. Each test is isolated in its own
/// temporary directory so it never touches the user's real config.
/// </summary>
public class StoreTests : IDisposable
{
    private readonly string _dir;

    public StoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "clemssh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup; a failed delete must not mask test failures
        }
    }

    private Store NewStore() => new Store(_dir);

    [Fact]
    public void Save_Then_Load_RoundTripsValue()
    {
        var store = NewStore();
        store.SaveEncrypted("server:example.com:22", "hunter2");

        bool ok = store.LoadEncrypted("server:example.com:22", out string? value);

        Assert.True(ok);
        Assert.Equal("hunter2", value);
    }

    [Fact]
    public void Load_UnknownKey_ReturnsFalse()
    {
        var store = NewStore();
        store.SaveEncrypted("server:example.com:22", "hunter2");

        bool ok = store.LoadEncrypted("host:example.com:22", out string? value);

        Assert.False(ok);
        Assert.Null(value);
    }

    [Fact]
    public void Keys_ReflectsSavedEntries()
    {
        var store = NewStore();
        store.SaveEncrypted("a:host1:22", "one");
        store.SaveEncrypted("b:host2:2222", "two");

        Assert.Contains("a:host1:22", store.Keys);
        Assert.Contains("b:host2:2222", store.Keys);
        Assert.Equal(2, store.Keys.Count);
    }

    [Fact]
    public void Save_OverwritesExistingValue()
    {
        var store = NewStore();
        store.SaveEncrypted("k:host:22", "old");

        store.SaveEncrypted("k:host:22", "new");

        Assert.True(store.LoadEncrypted("k:host:22", out string? value));
        Assert.Equal("new", value);
    }

    [Fact]
    public void Delete_RemovesValue()
    {
        var store = NewStore();
        store.SaveEncrypted("k:host:22", "secret");

        store.DeleteEncrypted("k:host:22");

        Assert.DoesNotContain("k:host:22", store.Keys);
        Assert.False(store.LoadEncrypted("k:host:22", out _));
    }

    [Fact]
    public void Delete_UnknownKey_IsNoOp()
    {
        var store = NewStore();
        store.SaveEncrypted("k:host:22", "secret");

        store.DeleteEncrypted("missing:host:22");

        Assert.True(store.LoadEncrypted("k:host:22", out string? value));
        Assert.Equal("secret", value);
    }

    [Fact]
    public void Store_IsPersistentAcrossInstances()
    {
        var first = NewStore();
        first.SaveEncrypted("k:host:22", "persisted");
        // simulate another process opening the same file
        var second = NewStore();

        Assert.True(second.LoadEncrypted("k:host:22", out string? value));
        Assert.Equal("persisted", value);
    }

    [Fact]
    public void StoredValue_IsEncrypted_NotPlaintext()
    {
        var store = NewStore();
        store.SaveEncrypted("k:host:22", "supersecretpassword");

        string raw = File.ReadAllText(Path.Combine(_dir, "clemssh", "store.json"));

        Assert.DoesNotContain("supersecretpassword", raw);
    }

    [Fact]
    public void EmptyValue_RoundTrips()
    {
        var store = NewStore();
        store.SaveEncrypted("k:host:22", "");

        Assert.True(store.LoadEncrypted("k:host:22", out string? value));
        Assert.Equal("", value);
    }

    [Fact]
    public void ValueWithSpecialCharacters_RoundTrips()
    {
        var store = NewStore();
        const string tricky = "p@ss w0rd\nwith\ttabs and $pecial ✨ ünicode";

        store.SaveEncrypted("k:host:22", tricky);

        Assert.True(store.LoadEncrypted("k:host:22", out string? value));
        Assert.Equal(tricky, value);
    }
}
