using Microsoft.Extensions.Configuration;
using TelegramPanel.Core.Services;
using TelegramPanel.Core.Utils;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Core.Services.Telegram;

public sealed record TelegramApiCredentials(int ApiId, string ApiHash, string? ProfileName = null);

public sealed record TelegramApiProfile(
    string Name,
    int ApiId,
    string ApiHash,
    bool Enabled = true,
    int Weight = 1,
    string? Notes = null);

public sealed class TelegramApiProfilePool
{
    private const int MaxWeight = 1000;
    private readonly IConfiguration _configuration;

    public TelegramApiProfilePool(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IReadOnlyList<TelegramApiProfile> GetConfiguredProfiles() => ReadConfiguredProfiles(_configuration);

    public IReadOnlyList<TelegramApiProfile> GetEnabledProfiles() => GetEnabledProfiles(_configuration);

    public bool HasUsableApi() => GetEnabledProfiles().Count > 0 || TryGetGlobalFallback(_configuration, out _);

    public async Task<TelegramApiCredentials> SelectForAccountAsync(
        Account? existingAccount,
        AccountManagementService accountManagement)
    {
        if (TryGetAccountCredentials(existingAccount, out var existingCredentials))
            return existingCredentials;

        var accounts = (await accountManagement.GetAllAccountsAsync()).ToList();
        return SelectForNewAccount(accounts);
    }

    public async Task<TelegramApiCredentials> SelectForNewAccountAsync(AccountManagementService accountManagement)
    {
        var accounts = (await accountManagement.GetAllAccountsAsync()).ToList();
        return SelectForNewAccount(accounts);
    }

    public TelegramApiCredentials SelectForNewAccount(IReadOnlyList<Account> existingAccounts)
    {
        var profiles = GetEnabledProfiles();
        if (profiles.Count == 0)
        {
            if (TryGetGlobalFallback(_configuration, out var fallback))
                return fallback;

            throw new InvalidOperationException("请先在【系统设置】中配置全局 Telegram API（ApiId/ApiHash）或至少一个启用的 API 配置");
        }

        var usage = new int[profiles.Count];
        foreach (var account in existingAccounts)
        {
            if (!TryGetAccountCredentials(account, out var credentials))
                continue;

            for (var i = 0; i < profiles.Count; i++)
            {
                if (SameCredentials(profiles[i], credentials))
                {
                    usage[i]++;
                    break;
                }
            }
        }

        var selectedIndex = 0;
        for (var i = 1; i < profiles.Count; i++)
        {
            var left = (long)usage[i] * profiles[selectedIndex].Weight;
            var right = (long)usage[selectedIndex] * profiles[i].Weight;
            if (left < right)
                selectedIndex = i;
        }

        var selected = profiles[selectedIndex];
        return new TelegramApiCredentials(selected.ApiId, selected.ApiHash, selected.Name);
    }

    public static IReadOnlyList<TelegramApiProfile> ReadConfiguredProfiles(IConfiguration configuration)
    {
        var profiles = new List<TelegramApiProfile>();
        var index = 0;
        foreach (var child in configuration.GetSection("Telegram:ApiProfiles").GetChildren())
        {
            var name = (child["Name"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = $"API {index + 1}";

            var apiIdText = (child["ApiId"] ?? string.Empty).Trim();
            var apiHash = (child["ApiHash"] ?? string.Empty).Trim();
            var enabledText = (child["Enabled"] ?? string.Empty).Trim();
            var weightText = (child["Weight"] ?? string.Empty).Trim();
            var notes = (child["Notes"] ?? string.Empty).Trim();

            profiles.Add(new TelegramApiProfile(
                name,
                int.TryParse(apiIdText, out var apiId) ? apiId : 0,
                apiHash,
                string.IsNullOrWhiteSpace(enabledText) || bool.TryParse(enabledText, out var enabled) && enabled,
                int.TryParse(weightText, out var weight) ? weight : 1,
                string.IsNullOrWhiteSpace(notes) ? null : notes));
            index++;
        }

        return profiles;
    }


    public static IReadOnlyList<TelegramApiProfile> GetEnabledProfiles(IConfiguration configuration)
    {
        return ReadConfiguredProfiles(configuration)
            .Where(profile => profile.Enabled)
            .Select(profile => TryNormalize(profile, out var normalized) ? normalized : null)
            .Where(profile => profile != null)
            .Cast<TelegramApiProfile>()
            .ToList();
    }

    public static bool TryGetAccountCredentials(Account? account, out TelegramApiCredentials credentials)
    {
        credentials = default!;
        if (account == null)
            return false;

        if (!TryNormalizeCredentials(account.ApiId, account.ApiHash, out var normalizedHash, out _))
            return false;

        credentials = new TelegramApiCredentials(account.ApiId, normalizedHash);
        return true;
    }

    public static bool TryGetGlobalFallback(IConfiguration configuration, out TelegramApiCredentials credentials)
    {
        credentials = default!;
        if (!int.TryParse(configuration["Telegram:ApiId"], out var apiId))
            return false;

        if (!TryNormalizeCredentials(apiId, configuration["Telegram:ApiHash"], out var apiHash, out _))
            return false;

        credentials = new TelegramApiCredentials(apiId, apiHash, "默认 API");
        return true;
    }

    public static bool TrySelectDefault(IConfiguration configuration, out TelegramApiCredentials credentials, out string error)
    {
        var profiles = GetEnabledProfiles(configuration);
        if (profiles.Count > 0)
        {
            var profile = profiles[0];
            credentials = new TelegramApiCredentials(profile.ApiId, profile.ApiHash, profile.Name);
            error = string.Empty;
            return true;
        }

        if (TryGetGlobalFallback(configuration, out credentials))
        {
            error = string.Empty;
            return true;
        }

        error = "请先在【系统设置】中配置全局 Telegram API（ApiId/ApiHash）或至少一个启用的 API 配置";
        return false;
    }

    public static bool TryNormalizeCredentials(int apiId, string? apiHash, out string normalizedApiHash, out string? reason)
    {
        normalizedApiHash = string.Empty;
        if (apiId <= 0)
        {
            reason = "ApiId 无效（必须为正整数）";
            return false;
        }

        if (!TelegramApiConfigValidator.TryNormalizeApiHash(apiHash, out normalizedApiHash, out reason))
            return false;

        reason = null;
        return true;
    }

    private static bool TryNormalize(TelegramApiProfile profile, out TelegramApiProfile normalized)
    {
        normalized = profile;
        if (!TryNormalizeCredentials(profile.ApiId, profile.ApiHash, out var apiHash, out _))
            return false;

        normalized = profile with
        {
            Name = string.IsNullOrWhiteSpace(profile.Name) ? $"API {profile.ApiId}" : profile.Name.Trim(),
            ApiHash = apiHash,
            Weight = Math.Clamp(profile.Weight, 1, MaxWeight),
            Notes = string.IsNullOrWhiteSpace(profile.Notes) ? null : profile.Notes.Trim()
        };
        return true;
    }

    private static bool SameCredentials(TelegramApiProfile profile, TelegramApiCredentials credentials) =>
        profile.ApiId == credentials.ApiId
        && string.Equals(profile.ApiHash, credentials.ApiHash, StringComparison.OrdinalIgnoreCase);
}
