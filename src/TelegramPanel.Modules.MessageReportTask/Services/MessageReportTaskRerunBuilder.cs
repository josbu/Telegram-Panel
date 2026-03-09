using System.Text.Json;
using TelegramPanel.Modules;

namespace TelegramPanel.Modules.MessageReportTask.Services;

public sealed class MessageReportTaskRerunBuilder : IModuleTaskRerunBuilder
{
    public string TaskType => MessageReportTaskConstants.TaskType;

    public ModuleTaskCreateRequest Build(ModuleTaskSnapshot task)
    {
        var rerunConfig = BuildRerunConfig(task.Config);
        var total = rerunConfig.MaxReports > 0 ? rerunConfig.MaxReports : 0;
        var configJson = JsonSerializer.Serialize(rerunConfig, new JsonSerializerOptions { WriteIndented = true });

        return new ModuleTaskCreateRequest
        {
            TaskType = MessageReportTaskConstants.TaskType,
            Total = total,
            Config = configJson
        };
    }

    private static MessageReportTaskConfig BuildRerunConfig(string? rawConfig)
    {
        var raw = (rawConfig ?? string.Empty).Trim();
        if (raw.Length == 0)
            throw new InvalidOperationException("任务配置为空，无法重新运行");

        MessageReportTaskConfig cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<MessageReportTaskConfig>(raw)
                  ?? throw new InvalidOperationException("任务配置解析结果为空");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"任务配置 JSON 无效：{ex.Message}");
        }

        cfg.CategoryIds = NormalizeCategoryIds(cfg.CategoryIds, cfg.CategoryId);
        if (cfg.CategoryIds.Count == 0)
            throw new InvalidOperationException("任务缺少账号分类，无法重新运行");

        cfg.CategoryId = cfg.CategoryIds[0];
        cfg.CategoryNames = NormalizeCategoryNames(cfg.CategoryNames, cfg.CategoryName);
        cfg.CategoryName = cfg.CategoryNames.FirstOrDefault() ?? cfg.CategoryName;

        cfg.MessageLinks = (cfg.MessageLinks ?? new List<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cfg.MessageLinks.Count == 0)
            throw new InvalidOperationException("任务缺少消息链接，无法重新运行");

        cfg.OptionKeywords = (cfg.OptionKeywords ?? new List<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cfg.DelayMinMs < 0) cfg.DelayMinMs = 0;
        if (cfg.DelayMaxMs < 0) cfg.DelayMaxMs = 0;
        if (cfg.DelayMinMs > 86400000) cfg.DelayMinMs = 86400000;
        if (cfg.DelayMaxMs > 86400000) cfg.DelayMaxMs = 86400000;
        if (cfg.DelayMaxMs < cfg.DelayMinMs) cfg.DelayMaxMs = cfg.DelayMinMs;
        if (cfg.MaxReports < 0) cfg.MaxReports = 0;

        cfg.ReportPreset = NormalizePreset(cfg.ReportPreset);
        cfg.Comment = NormalizeNullableText(cfg.Comment);
        cfg.Canceled = false;
        cfg.Error = null;
        cfg.RecentFailures = new List<MessageReportTaskRuntimeFailure>();

        return cfg;
    }

    private static List<int> NormalizeCategoryIds(IEnumerable<int>? values, int fallback)
    {
        var ids = (values ?? Array.Empty<int>())
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0 && fallback > 0)
            ids.Add(fallback);

        return ids;
    }

    private static List<string> NormalizeCategoryNames(IEnumerable<string>? values, string? fallback)
    {
        var names = (values ?? Array.Empty<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fallbackName = (fallback ?? string.Empty).Trim();
        if (names.Count == 0 && fallbackName.Length > 0)
            names.Add(fallbackName);

        return names;
    }

    private static string NormalizePreset(string? preset)
    {
        var value = (preset ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            MessageReportTaskPresets.Spam => MessageReportTaskPresets.Spam,
            MessageReportTaskPresets.Violence => MessageReportTaskPresets.Violence,
            MessageReportTaskPresets.Pornography => MessageReportTaskPresets.Pornography,
            MessageReportTaskPresets.ChildAbuse => MessageReportTaskPresets.ChildAbuse,
            MessageReportTaskPresets.Copyright => MessageReportTaskPresets.Copyright,
            MessageReportTaskPresets.IllegalDrugs => MessageReportTaskPresets.IllegalDrugs,
            MessageReportTaskPresets.PersonalDetails => MessageReportTaskPresets.PersonalDetails,
            MessageReportTaskPresets.Other => MessageReportTaskPresets.Other,
            MessageReportTaskPresets.FirstAvailable => MessageReportTaskPresets.FirstAvailable,
            MessageReportTaskPresets.Custom => MessageReportTaskPresets.Custom,
            _ => MessageReportTaskPresets.Spam
        };
    }

    private static string? NormalizeNullableText(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length == 0 ? null : text;
    }
}
