using System.Globalization;
using System.Text.Json.Serialization;

namespace TelegramPanel.Modules.MessageReport;

public static class MessageReportTaskTypes
{
    public const string TaskType = "module_message_report";
}

public static class MessageReportTaskModes
{
    public const string Random = "random";
    public const string Queue = "queue";

    public static string Normalize(string? value)
    {
        return string.Equals((value ?? string.Empty).Trim(), Queue, StringComparison.OrdinalIgnoreCase)
            ? Queue
            : Random;
    }
}

public static class MessageReportTaskReportTypes
{
    public const string Auto = "auto";
    public const string Spam = "spam";
    public const string Violence = "violence";
    public const string Pornography = "pornography";
    public const string ChildAbuse = "child_abuse";
    public const string Copyright = "copyright";
    public const string PersonalDetails = "personal_details";
    public const string IllegalDrugs = "illegal_drugs";
    public const string Fake = "fake";
    public const string Other = "other";

    public static string Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            Spam or Violence or Pornography or ChildAbuse or Copyright or PersonalDetails or IllegalDrugs or Fake or Other => normalized,
            _ => Auto
        };
    }
}

public sealed class MessageReportTaskConfig
{
    [JsonPropertyName("category_id")]
    public int CategoryId { get; set; }

    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }

    [JsonPropertyName("category_ids")]
    public List<int> CategoryIds { get; set; } = new();

    [JsonPropertyName("category_names")]
    public List<string> CategoryNames { get; set; } = new();

    [JsonPropertyName("message_references")]
    public List<string> MessageReferences { get; set; } = new();

    [JsonPropertyName("delay_min_ms")]
    public int DelayMinMs { get; set; } = 15000;

    [JsonPropertyName("delay_max_ms")]
    public int DelayMaxMs { get; set; } = 45000;

    [JsonPropertyName("max_reports")]
    public int MaxReports { get; set; }

    [JsonPropertyName("report_type")]
    public string ReportType { get; set; } = MessageReportTaskReportTypes.Spam;

    [JsonPropertyName("option_keywords")]
    public List<string> OptionKeywords { get; set; } = new();

    [JsonPropertyName("comments")]
    public List<string> Comments { get; set; } = new();

    [JsonPropertyName("comment_mode")]
    public string CommentMode { get; set; } = MessageReportTaskModes.Queue;

    [JsonPropertyName("completed_attempts")]
    public int CompletedAttempts { get; set; }

    [JsonPropertyName("failed_attempts")]
    public int FailedAttempts { get; set; }

    [JsonPropertyName("account_cursor")]
    public int AccountCursor { get; set; }

    [JsonPropertyName("message_cursor")]
    public int MessageCursor { get; set; }

    [JsonPropertyName("comment_cursor")]
    public int CommentCursor { get; set; }

    [JsonPropertyName("canceled")]
    public bool Canceled { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("recent_failures")]
    public List<MessageReportTaskRuntimeFailure> RecentFailures { get; set; } = new();
}

public sealed class MessageReportTaskRuntimeFailure
{
    [JsonPropertyName("timestamp_utc")]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("account_id")]
    public int AccountId { get; set; }

    [JsonPropertyName("account_display")]
    public string AccountDisplay { get; set; } = string.Empty;

    [JsonPropertyName("message_reference")]
    public string MessageReference { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

public sealed record ParsedMessageReference(string Raw, string Target, int MessageId, string CanonicalId);

public static class MessageReportTaskInputHelper
{
    public static List<int> NormalizeCategoryIds(IEnumerable<int>? values, int fallback)
    {
        var ids = (values ?? Array.Empty<int>())
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0 && fallback > 0)
            ids.Add(fallback);

        return ids;
    }

    public static List<string> NormalizeCategoryNames(IEnumerable<string>? values, string? fallback)
    {
        var names = NormalizeLines(values, distinct: true);
        var fallbackName = (fallback ?? string.Empty).Trim();
        if (names.Count == 0 && fallbackName.Length > 0)
            names.Add(fallbackName);
        return names;
    }

    public static List<string> NormalizeLines(IEnumerable<string>? values, bool distinct = false)
    {
        var lines = (values ?? Array.Empty<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .ToList();

        return distinct
            ? lines.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : lines;
    }

    public static List<string> ParseMultilineText(string? text, bool distinct = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        return NormalizeLines(
            text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            distinct);
    }

    public static bool TryParseMessageReference(string? raw, out ParsedMessageReference? parsed, out string? error)
    {
        parsed = null;
        error = null;

        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            error = "消息引用不能为空";
            return false;
        }

        value = NormalizeTelegramPath(value);
        if (value.Length == 0)
        {
            error = "消息引用格式无效";
            return false;
        }

        var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string target;
        string messageIdText;

        if (parts.Length >= 3 && string.Equals(parts[0], "c", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var internalChatId) || internalChatId <= 0)
            {
                error = "t.me/c 链接中的频道 ID 无效";
                return false;
            }

            target = $"-100{internalChatId}";
            messageIdText = parts[2];
        }
        else if (parts.Length >= 2)
        {
            target = parts[0];
            messageIdText = parts[1];
        }
        else
        {
            error = "消息引用格式无效，应类似 username/123 或 t.me/c/1234567890/123";
            return false;
        }

        target = target.Trim().TrimStart('@');
        if (target.Length == 0)
        {
            error = "消息引用缺少目标";
            return false;
        }

        if (!int.TryParse(messageIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var messageId) || messageId <= 0)
        {
            error = "消息 ID 无效，必须是正整数";
            return false;
        }

        parsed = new ParsedMessageReference((raw ?? string.Empty).Trim(), target, messageId, $"{target}/{messageId}");
        return true;
    }

    private static string NormalizeTelegramPath(string value)
    {
        var raw = value.Trim();
        if (Uri.TryCreate(raw, UriKind.Absolute, out var absolute))
        {
            var host = (absolute.Host ?? string.Empty).Trim().ToLowerInvariant();
            if (host is not ("t.me" or "www.t.me" or "telegram.me" or "www.telegram.me"))
                return string.Empty;

            raw = absolute.PathAndQuery;
        }
        else if (raw.StartsWith("t.me/", StringComparison.OrdinalIgnoreCase)
                 || raw.StartsWith("telegram.me/", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[(raw.IndexOf('/') + 1)..];
        }

        var questionMarkIndex = raw.IndexOf('?');
        if (questionMarkIndex >= 0)
            raw = raw[..questionMarkIndex];

        raw = raw.Trim().Trim('/').Replace('\\', '/');
        if (raw.StartsWith("s/", StringComparison.OrdinalIgnoreCase))
            raw = raw[2..];

        return raw.Trim('/');
    }
}
