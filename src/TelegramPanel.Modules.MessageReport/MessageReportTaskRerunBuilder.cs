using System.Text.Json;
using TelegramPanel.Modules;

namespace TelegramPanel.Modules.MessageReport;

public sealed class MessageReportTaskRerunBuilder : IModuleTaskRerunBuilder
{
    public string TaskType => MessageReportTaskTypes.TaskType;

    public ModuleTaskCreateRequest Build(ModuleTaskSnapshot task)
    {
        var rerunConfig = BuildRerunConfig(task.Config);
        var total = rerunConfig.MaxReports > 0 ? rerunConfig.MaxReports : 0;

        return new ModuleTaskCreateRequest
        {
            TaskType = MessageReportTaskTypes.TaskType,
            Total = total,
            Config = JsonSerializer.Serialize(rerunConfig, new JsonSerializerOptions { WriteIndented = true })
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

        cfg.CategoryIds = MessageReportTaskInputHelper.NormalizeCategoryIds(cfg.CategoryIds, cfg.CategoryId);
        if (cfg.CategoryIds.Count == 0)
            throw new InvalidOperationException("任务缺少账号分类，无法重新运行");

        cfg.CategoryNames = MessageReportTaskInputHelper.NormalizeCategoryNames(cfg.CategoryNames, cfg.CategoryName);
        cfg.CategoryId = cfg.CategoryIds[0];
        cfg.CategoryName = cfg.CategoryNames.FirstOrDefault() ?? cfg.CategoryName;
        cfg.MessageReferences = MessageReportTaskInputHelper.NormalizeLines(cfg.MessageReferences, distinct: true);
        cfg.OptionKeywords = MessageReportTaskInputHelper.NormalizeLines(cfg.OptionKeywords, distinct: true);
        cfg.Comments = MessageReportTaskInputHelper.NormalizeLines(cfg.Comments);
        cfg.ReportType = MessageReportTaskReportTypes.Normalize(cfg.ReportType);
        cfg.CommentMode = MessageReportTaskModes.Normalize(cfg.CommentMode);

        foreach (var messageReference in cfg.MessageReferences)
        {
            if (!MessageReportTaskInputHelper.TryParseMessageReference(messageReference, out _, out var error))
                throw new InvalidOperationException($"消息引用无效：{messageReference}（{error}）");
        }

        if (cfg.MessageReferences.Count == 0)
            throw new InvalidOperationException("任务缺少消息引用，无法重新运行");

        cfg.DelayMinMs = Math.Clamp(cfg.DelayMinMs, 0, 600000);
        cfg.DelayMaxMs = Math.Clamp(cfg.DelayMaxMs, cfg.DelayMinMs, 600000);
        cfg.MaxReports = Math.Max(0, cfg.MaxReports);
        cfg.CompletedAttempts = 0;
        cfg.FailedAttempts = 0;
        cfg.AccountCursor = 0;
        cfg.MessageCursor = 0;
        cfg.CommentCursor = 0;
        cfg.Canceled = false;
        cfg.Error = null;
        cfg.RecentFailures = new List<MessageReportTaskRuntimeFailure>();
        return cfg;
    }
}
