using Microsoft.Extensions.Configuration;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Web.Api;
using TelegramPanel.Data.Entities;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class TelegramApiProfilePoolTests
{
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string HashC = "cccccccccccccccccccccccccccccccc";

    [Fact]
    public void SelectForNewAccount_BalancesAcrossEnabledProfilesByUsage()
    {
        var pool = CreatePool(("primary", 1001, HashA, true, 1), ("secondary", 1002, HashB, true, 1));
        var accounts = new List<Account>
        {
            new() { ApiId = 1001, ApiHash = HashA },
            new() { ApiId = 1001, ApiHash = HashA },
            new() { ApiId = 1002, ApiHash = HashB }
        };

        var selected = pool.SelectForNewAccount(accounts);

        Assert.Equal(1002, selected.ApiId);
        Assert.Equal(HashB, selected.ApiHash);
        Assert.Equal("secondary", selected.ProfileName);
    }

    [Fact]
    public void SelectForNewAccount_SkipsDisabledProfiles()
    {
        var pool = CreatePool(("disabled", 1001, HashA, false, 1), ("enabled", 1002, HashB, true, 1));

        var selected = pool.SelectForNewAccount(Array.Empty<Account>());

        Assert.Equal(1002, selected.ApiId);
        Assert.Equal(HashB, selected.ApiHash);
    }

    [Fact]
    public void SelectForAccount_PreservesExistingAccountApi()
    {
        var existing = new Account { ApiId = 2001, ApiHash = HashC };

        var preserved = TelegramApiProfilePool.TryGetAccountCredentials(existing, out var credentials);

        Assert.True(preserved);
        Assert.Equal(2001, credentials.ApiId);
        Assert.Equal(HashC, credentials.ApiHash);
    }

    [Fact]
    public void SelectForNewAccount_FallsBackToSingleGlobalApi()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:ApiId"] = "3001",
                ["Telegram:ApiHash"] = HashA
            })
            .Build();
        var pool = new TelegramApiProfilePool(configuration);

        var selected = pool.SelectForNewAccount(Array.Empty<Account>());

        Assert.Equal(3001, selected.ApiId);
        Assert.Equal(HashA, selected.ApiHash);
        Assert.Equal("默认 API", selected.ProfileName);
    }

    [Fact]
    public void NormalizeTelegramApiDefaultInput_TreatsZeroWithoutHashAsProfileOnly()
    {
        var result = PanelAdminApiEndpoints.NormalizeTelegramApiDefaultInput("0", " ");

        Assert.False(result.HasDefaultApi);
        Assert.Equal(string.Empty, result.ApiId);
        Assert.Equal(string.Empty, result.ApiHash);
    }


    private static TelegramApiProfilePool CreatePool(params (string Name, int ApiId, string ApiHash, bool Enabled, int Weight)[] profiles)
    {
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < profiles.Length; i++)
        {
            var profile = profiles[i];
            values[$"Telegram:ApiProfiles:{i}:Name"] = profile.Name;
            values[$"Telegram:ApiProfiles:{i}:ApiId"] = profile.ApiId.ToString();
            values[$"Telegram:ApiProfiles:{i}:ApiHash"] = profile.ApiHash;
            values[$"Telegram:ApiProfiles:{i}:Enabled"] = profile.Enabled.ToString();
            values[$"Telegram:ApiProfiles:{i}:Weight"] = profile.Weight.ToString();
        }

        return new TelegramApiProfilePool(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }
}
