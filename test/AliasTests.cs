using ClemSSH.Lib;
using Xunit;

namespace ClemSSH.Tests;

public class AliasTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "clemssh-alias-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AliasesPersistAndCanBeUpdatedAndRemovedRegardlessOfCase()
    {
        var store = new AliasStore(directory);
        Assert.Empty(store.List());
        Assert.False(store.Remove("prod"));
        store.Set("Prod", "deploy@example.com:2222");

        store = new AliasStore(directory);
        Assert.Equal("deploy@example.com:2222", store.Resolve("PROD"));
        store.Set("PROD", "admin@example.net");
        Assert.Equal(new KeyValuePair<string, string>("prod", "admin@example.net"), Assert.Single(store.List()));

        Assert.True(store.Remove("pRoD"));
        Assert.Empty(new AliasStore(directory).List());
        Assert.Equal("prod", store.Resolve("prod"));
    }

    [Fact]
    public void OnlyBareNamesResolveAndTargetsAreLiteral()
    {
        var store = new AliasStore(directory);
        store.Set("prod", "server");
        store.Set("server", "example.com");

        Assert.Equal("server", store.Resolve("prod"));
        Assert.Equal("user@prod", store.Resolve("user@prod"));
        Assert.Equal("prod:22", store.Resolve("prod:22"));
    }

    [Theory]
    [InlineData("../prod")]
    [InlineData("--help")]
    [InlineData("example.com")]
    [InlineData("user@server")]
    public void InvalidNamesAreRejected(string name)
    {
        Assert.Throws<ArgumentException>(() => new AliasStore(directory).Set(name, "example.com"));
    }

    [Theory]
    [InlineData("ssh://example.com")]
    [InlineData("user@@example.com")]
    [InlineData("example.com:0")]
    [InlineData("example.com:65536")]
    [InlineData("example.com:99999999999")]
    [InlineData("example.com\nother.example.com")]
    public void InvalidTargetsAreRejectedWithoutCreatingAnAlias(string target)
    {
        var store = new AliasStore(directory);
        Assert.Throws<ArgumentException>(() => store.Set("prod", target));
        Assert.Empty(store.List());
    }

    [Fact]
    public void ResolvedDestinationCanFindMatchingSavedCredentials()
    {
        var store = new AliasStore(directory);
        store.Set("prod", "deploy@example.com:2222");
        Assert.True(SshDestination.TryParse(store.Resolve("prod"), out var target));

        var matches = KeySearcher.FindMatches(target,
            ["deploy@EXAMPLE.COM:2222", "deploy@example.com:22", "admin@example.com:2222", "malformed"]);
        Assert.Equal("deploy@EXAMPLE.COM:2222", Assert.Single(matches));
    }
}
