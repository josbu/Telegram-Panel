using System.Text.Json.Serialization;

namespace TelegramPanel.Modules.MessageReportTask.Services;

public static class MessageReportTaskConstants
{
    public const string ModuleId = "task.message-report";
    public const string TaskType = "user_message_report";
}

public static class MessageReportTaskPresets
{
    public const string Spam = "spam";
    public const string Violence = "violence";
    public const string Pornography = "pornography";
    public const string ChildAbuse = "child_abuse";
    public const string Copyright = "copyright";
    public const string IllegalDrugs = "illegal_drugs";
    public const string PersonalDetails = "personal_details";
    public const string Other = "other";
    public const string FirstAvailable = "first_available";
    public const string Custom = "custom";
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

    [JsonPropertyName("message_links")]
    public List<string> MessageLinks { get; set; } = new();

    [JsonPropertyName("delay_min_ms")]
    public int DelayMinMs { get; set; } = 15000;

    [JsonPropertyName("delay_max_ms")]
    public int DelayMaxMs { get; set; } = 45000;

    [JsonPropertyName("max_reports")]
    public int MaxReports { get; set; }

    [JsonPropertyName("report_preset")]
    public string ReportPreset { get; set; } = MessageReportTaskPresets.Spam;

    [JsonPropertyName("option_keywords")]
    public List<string> OptionKeywords { get; set; } = new();

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("canceled")]
    public bool Canceled { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("recent_failures")]
    public List<MessageReportTaskRuntimeFailure> RecentFailures { get; set; } = new();
}

public sealed class MessageReportTaskRuntimeFailure
{
    [JsonPropertyName("time_utc")]
    public DateTime TimeUtc { get; set; }

    [JsonPropertyName("account_id")]
    public int AccountId { get; set; }

    [JsonPropertyName("account")]
    public string Account { get; set; } = string.Empty;

    [JsonPropertyName("message_link")]
    public string MessageLink { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}
