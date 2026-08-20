using System.Text.Json.Serialization;
using TelegramPanel.Core.Models;

namespace TelegramPanel.Web.Services;

public sealed class AutoChangeLoginEmailTaskConfig
{
    [JsonPropertyName("category_ids")]
    public List<int> CategoryIds { get; set; } = new();

    [JsonPropertyName("category_names")]
    public List<string> CategoryNames { get; set; } = new();

    [JsonPropertyName("account_numbers")]
    public List<int> AccountNumbers { get; set; } = new();


    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("domains")]
    public List<string> Domains { get; set; } = new();

    [JsonPropertyName("trigger_days_ago")]
    public int TriggerDaysAgo { get; set; } = 6;

    [JsonPropertyName("trigger_window_hours")]
    public int TriggerWindowHours { get; set; } = 24;

    [JsonPropertyName("max_system_messages")]
    public int MaxSystemMessages { get; set; } = 300;

    [JsonPropertyName("force")]
    public bool Force { get; set; }

    [JsonPropertyName("auto_confirm")]
    public bool AutoConfirm { get; set; } = true;

    [JsonPropertyName("poll_interval_seconds")]
    public int PollIntervalSeconds { get; set; } = 5;

    [JsonPropertyName("poll_timeout_seconds")]
    public int PollTimeoutSeconds { get; set; } = 90;

    [JsonPropertyName("trigger_phrases")]
    public List<string> TriggerPhrases { get; set; } = new();

    [JsonPropertyName("requested_at_utc")]
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("items")]
    public List<AutoChangeLoginEmailTaskItem> Items { get; set; } = new();
}

public sealed class AutoChangeLoginEmailTaskItem
{
    [JsonPropertyName("time_utc")]
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("account_id")]
    public int AccountId { get; set; }

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("target_domain")]
    public string? TargetDomain { get; set; }

    [JsonPropertyName("previous_login_email_pattern")]
    public string? PreviousLoginEmailPattern { get; set; }

    [JsonPropertyName("previous_login_email_domain")]
    public string? PreviousLoginEmailDomain { get; set; }

    [JsonPropertyName("result")]
    public string Result { get; set; } = AutoChangeLoginEmailTaskResult.Skipped;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("matched_message_id")]
    public int? MatchedMessageId { get; set; }

    [JsonPropertyName("matched_message_date_utc")]
    public DateTime? MatchedMessageDateUtc { get; set; }
}

public static class AutoChangeLoginEmailTaskResult
{
    public const string Success = "success";
    public const string Skipped = "skipped";
    public const string Failed = "failed";
}

internal sealed record AutoChangeLoginEmailNoticeMatch(int MessageId, DateTime DateUtc, string Text);

internal sealed record AutoChangeLoginEmailDecision(
    bool ShouldAttempt,
    string Result,
    string Message,
    AutoChangeLoginEmailNoticeMatch? Match);

internal static class AutoChangeLoginEmailNoticeDetector
{
    private static readonly string[] DefaultTriggerPhrases =
    [
        "settings privacy security login email",
        "settings privacy and security login email",
        "settings > privacy & security > login email",
        "login email reset",
        "reset login email"
    ];

    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) BuildWindowUtc(
        DateTimeOffset nowUtc,
        int triggerDaysAgo,
        int triggerWindowHours)
    {
        triggerDaysAgo = Math.Clamp(triggerDaysAgo, 0, 30);
        triggerWindowHours = Math.Clamp(triggerWindowHours, 1, 24 * 14);

        var center = nowUtc.ToUniversalTime().AddDays(-triggerDaysAgo);
        var half = TimeSpan.FromHours(triggerWindowHours / 2.0d);
        return (center - half, center + half);
    }

    public static AutoChangeLoginEmailNoticeMatch? FindBestMatch(
        IEnumerable<TelegramSystemMessage> messages,
        DateTimeOffset nowUtc,
        int triggerDaysAgo,
        int triggerWindowHours,
        IEnumerable<string>? triggerPhrases = null)
    {
        var (fromUtc, toUtc) = BuildWindowUtc(nowUtc, triggerDaysAgo, triggerWindowHours);
        var phrases = NormalizePhrases(triggerPhrases).ToArray();

        return (messages ?? Array.Empty<TelegramSystemMessage>())
            .Select(message => ToMatchCandidate(message, fromUtc, toUtc, phrases))
            .Where(match => match != null)
            .OrderByDescending(match => match!.DateUtc)
            .FirstOrDefault();
    }

    public static AutoChangeLoginEmailDecision Decide(
        bool cloudMailConfigured,
        string? targetEmail,
        bool force,
        IEnumerable<TelegramSystemMessage> messages,
        DateTimeOffset nowUtc,
        int triggerDaysAgo,
        int triggerWindowHours,
        IEnumerable<string>? triggerPhrases = null)
    {
        if (!cloudMailConfigured)
        {
            return new AutoChangeLoginEmailDecision(
                ShouldAttempt: false,
                Result: AutoChangeLoginEmailTaskResult.Skipped,
                Message: "已跳过：未配置 Cloud Mail URL/Token/邮箱域名",
                Match: null);
        }

        if (string.IsNullOrWhiteSpace(targetEmail))
        {
            return new AutoChangeLoginEmailDecision(
                ShouldAttempt: false,
                Result: AutoChangeLoginEmailTaskResult.Skipped,
                Message: "已跳过：无法按手机号生成目标邮箱",
                Match: null);
        }

        if (force)
        {
            return new AutoChangeLoginEmailDecision(
                ShouldAttempt: true,
                Result: AutoChangeLoginEmailTaskResult.Success,
                Message: "强制模式：未要求匹配 777000 登录邮箱重置通知",
                Match: null);
        }

        var match = FindBestMatch(messages, nowUtc, triggerDaysAgo, triggerWindowHours, triggerPhrases);
        if (match == null)
        {
            var (fromUtc, toUtc) = BuildWindowUtc(nowUtc, triggerDaysAgo, triggerWindowHours);
            return new AutoChangeLoginEmailDecision(
                ShouldAttempt: false,
                Result: AutoChangeLoginEmailTaskResult.Skipped,
                Message: $"已跳过：未在 {fromUtc:yyyy-MM-dd HH:mm}~{toUtc:yyyy-MM-dd HH:mm} UTC 发现登录邮箱重置通知",
                Match: null);
        }

        return new AutoChangeLoginEmailDecision(
            ShouldAttempt: true,
            Result: AutoChangeLoginEmailTaskResult.Success,
            Message: $"匹配到 777000 登录邮箱重置通知 #{match.MessageId}（{match.DateUtc:yyyy-MM-dd HH:mm} UTC）",
            Match: match);
    }

    private static AutoChangeLoginEmailNoticeMatch? ToMatchCandidate(
        TelegramSystemMessage message,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyList<string> triggerPhrases)
    {
        if (message.DateUtc == null || string.IsNullOrWhiteSpace(message.Text))
            return null;

        var dateUtc = DateTime.SpecifyKind(message.DateUtc.Value, DateTimeKind.Utc);
        var offset = new DateTimeOffset(dateUtc);
        if (offset < fromUtc || offset > toUtc)
            return null;

        var normalized = Normalize(message.Text);
        if (!LooksLikeLoginEmailResetNotice(normalized, triggerPhrases))
            return null;

        return new AutoChangeLoginEmailNoticeMatch(message.Id, dateUtc, message.Text.Trim());
    }

    private static bool LooksLikeLoginEmailResetNotice(string normalizedText, IReadOnlyList<string> triggerPhrases)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
            return false;

        foreach (var phrase in triggerPhrases)
        {
            if (normalizedText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var mentionsLoginEmail = normalizedText.Contains("login email", StringComparison.OrdinalIgnoreCase);
        if (!mentionsLoginEmail)
            return false;

        var mentionsSettingsPath = normalizedText.Contains("settings", StringComparison.OrdinalIgnoreCase)
            && normalizedText.Contains("privacy", StringComparison.OrdinalIgnoreCase)
            && normalizedText.Contains("security", StringComparison.OrdinalIgnoreCase);
        var mentionsReset = normalizedText.Contains("reset", StringComparison.OrdinalIgnoreCase)
            || normalizedText.Contains("change", StringComparison.OrdinalIgnoreCase)
            || normalizedText.Contains("requested", StringComparison.OrdinalIgnoreCase);

        return mentionsSettingsPath && mentionsReset;
    }

    private static IEnumerable<string> NormalizePhrases(IEnumerable<string>? triggerPhrases)
    {
        var any = false;
        foreach (var phrase in triggerPhrases ?? Array.Empty<string>())
        {
            var normalized = Normalize(phrase);
            if (normalized.Length == 0)
                continue;
            any = true;
            yield return normalized;
        }

        if (any)
            yield break;

        foreach (var phrase in DefaultTriggerPhrases)
            yield return Normalize(phrase);
    }

    private static string Normalize(string value)
    {
        value = (value ?? string.Empty).ToLowerInvariant().Replace('&', ' ');
        var chars = new char[value.Length];
        var count = 0;
        var pendingSpace = false;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && count > 0)
                    chars[count++] = ' ';
                chars[count++] = ch;
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return count == 0 ? string.Empty : new string(chars, 0, count).Trim();
    }
}
