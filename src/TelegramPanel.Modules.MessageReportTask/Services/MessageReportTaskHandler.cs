using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Services;
using TelegramPanel.Data.Entities;
using TelegramPanel.Modules;

namespace TelegramPanel.Modules.MessageReportTask.Services;

public sealed class MessageReportTaskHandler : IModuleTaskHandler
{
    private const int MaxFailureLines = 50;

    public string TaskType => MessageReportTaskConstants.TaskType;

    public async Task ExecuteAsync(IModuleTaskExecutionHost host, CancellationToken cancellationToken)
    {
        var logger = host.Services.GetRequiredService<ILogger<MessageReportTaskHandler>>();
        var taskManagement = host.Services.GetRequiredService<BatchTaskManagementService>();
        var accountManagement = host.Services.GetRequiredService<AccountManagementService>();
        var reportService = host.Services.GetRequiredService<TelegramMessageReportService>();

        var config = DeserializeConfig(host.Config);
        ValidateAndNormalizeConfig(config);

        var selectedCategoryIds = NormalizeSelectedCategoryIds(config).ToHashSet();
        var allAccounts = (await accountManagement.GetAllAccountsAsync())
            .Where(x => x.IsActive && x.UserId > 0 && x.Category?.ExcludeFromOperations != true)
            .Where(x => x.CategoryId.HasValue && selectedCategoryIds.Contains(x.CategoryId.Value))
            .OrderBy(x => x.Id)
            .ToList();

        if (allAccounts.Count() == 0)
            throw new InvalidOperationException("所选分类下没有可用执行账号");

        var accountSlots = new List<AccountSlot>();
        foreach (var account in allAccounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await host.IsStillRunningAsync(cancellationToken))
            {
                config.Canceled = true;
                await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));
                return;
            }

            var slot = new AccountSlot(account);
            foreach (var rawLink in config.MessageLinks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await host.IsStillRunningAsync(cancellationToken))
                {
                    config.Canceled = true;
                    await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));
                    return;
                }

                var resolved = await reportService.ResolveMessageTargetAsync(account.Id, rawLink, cancellationToken);
                if (resolved.Success && resolved.Target != null)
                {
                    slot.Targets.Add(new TargetSlot(resolved.Target));
                    continue;
                }

                AddFailure(config, account, rawLink, NormalizeReason(resolved.Error));
            }

            if (slot.Targets.Count > 0)
                accountSlots.Add(slot);
        }

        if (accountSlots.Count == 0)
        {
            config.Error = "没有可用的账号-消息组合（请确认这些账号能访问目标消息）";
            await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));
            throw new InvalidOperationException(config.Error);
        }

        config.Error = null;
        await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));

        var completed = 0;
        var failed = 0;
        var accountQueueIndex = 0;
        var targetQueueIndexByAccountId = new Dictionary<int, int>();
        var lastProgressPersistAt = DateTime.UtcNow;

        try
        {
            while (!cancellationToken.IsCancellationRequested && accountSlots.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!await host.IsStillRunningAsync(cancellationToken))
                {
                    config.Canceled = true;
                    break;
                }

                if (config.MaxReports > 0 && completed >= config.MaxReports)
                    break;

                var accountIdx = SelectNextAccountIndex(accountSlots, ref accountQueueIndex);
                if (accountIdx < 0)
                    break;

                var accountSlot = accountSlots[accountIdx];
                var targetIdx = SelectNextTargetIndex(accountSlot, targetQueueIndexByAccountId);
                if (targetIdx < 0)
                {
                    accountSlots.RemoveAt(accountIdx);
                    targetQueueIndexByAccountId.Remove(accountSlot.Account.Id);
                    continue;
                }

                var targetSlot = accountSlot.Targets[targetIdx];
                var result = await reportService.ReportMessageAsync(accountSlot.Account.Id, targetSlot.Target, config, cancellationToken);

                completed++;
                var hadFailureThisRound = false;
                var shouldRemoveTarget = result.Success || result.ShouldRemoveTarget;

                if (!result.Success)
                {
                    failed++;
                    hadFailureThisRound = true;
                    targetSlot.ConsecutiveFailures++;
                    AddFailure(config, accountSlot.Account, targetSlot.Target.RawInput, NormalizeReason(result.Error));
                    if (targetSlot.ConsecutiveFailures >= 3)
                        shouldRemoveTarget = true;

                    await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));
                }

                if (shouldRemoveTarget)
                {
                    accountSlot.Targets.RemoveAt(targetIdx);
                    if (accountSlot.Targets.Count == 0)
                    {
                        accountSlots.RemoveAt(accountIdx);
                        targetQueueIndexByAccountId.Remove(accountSlot.Account.Id);
                    }
                }

                if (ShouldPersistProgress(completed, hadFailureThisRound, lastProgressPersistAt))
                {
                    await host.UpdateProgressAsync(completed, failed, cancellationToken);
                    lastProgressPersistAt = DateTime.UtcNow;
                }

                if (config.MaxReports > 0 && completed >= config.MaxReports)
                    break;

                if (accountSlots.Count == 0)
                    break;

                var delayMs = NextDelayMilliseconds(config.DelayMinMs, config.DelayMaxMs);
                if (delayMs > 0)
                {
                    var keepRunning = await DelayWithTaskCheckAsync(host, delayMs, cancellationToken);
                    if (!keepRunning)
                    {
                        config.Canceled = true;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MessageReport task failed (taskId={TaskId})", host.TaskId);
            config.Error = ex.Message;
            await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));
            throw;
        }

        if (config.MaxReports > 0 && accountSlots.Count == 0 && completed < host.Total)
            await taskManagement.UpdateTaskDraftAsync(host.TaskId, completed, SerializeIndented(config));
        else
            await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));

        await host.UpdateProgressAsync(completed, failed, cancellationToken);
        if (config.Canceled)
        {
            await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));
            return;
        }

        config.Error = null;
        await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));
    }

    private static MessageReportTaskConfig DeserializeConfig(string? rawConfig)
    {
        var raw = (rawConfig ?? string.Empty).Trim();
        if (raw.Length == 0)
            throw new InvalidOperationException("任务配置为空");

        try
        {
            return JsonSerializer.Deserialize<MessageReportTaskConfig>(raw)
                   ?? throw new InvalidOperationException("任务配置解析结果为空");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"任务配置 JSON 无效：{ex.Message}");
        }
    }

    private static void ValidateAndNormalizeConfig(MessageReportTaskConfig config)
    {
        config.CategoryIds = NormalizeSelectedCategoryIds(config);
        if (config.CategoryIds.Count == 0)
            throw new InvalidOperationException("请至少选择一个执行账号分类");

        config.CategoryId = config.CategoryIds[0];
        config.CategoryNames = NormalizeCategoryNames(config.CategoryNames, config.CategoryName);
        config.CategoryName = config.CategoryNames.FirstOrDefault() ?? config.CategoryName;

        config.MessageLinks = (config.MessageLinks ?? new List<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (config.MessageLinks.Count == 0)
            throw new InvalidOperationException("请至少填写一条消息链接");

        config.OptionKeywords = (config.OptionKeywords ?? new List<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        config.ReportPreset = NormalizePreset(config.ReportPreset);
        if (config.ReportPreset == MessageReportTaskPresets.Custom && config.OptionKeywords.Count == 0)
            throw new InvalidOperationException("选择“自定义关键词”时，至少填写一个关键词");

        config.Comment = NormalizeNullableText(config.Comment);
        if (config.DelayMinMs < 0) config.DelayMinMs = 0;
        if (config.DelayMaxMs < 0) config.DelayMaxMs = 0;
        if (config.DelayMinMs > 86400000) config.DelayMinMs = 86400000;
        if (config.DelayMaxMs > 86400000) config.DelayMaxMs = 86400000;
        if (config.DelayMaxMs < config.DelayMinMs) config.DelayMaxMs = config.DelayMinMs;
        if (config.MaxReports < 0) config.MaxReports = 0;

        config.Canceled = false;
        config.Error = NormalizeNullableText(config.Error);
        config.RecentFailures ??= new List<MessageReportTaskRuntimeFailure>();
    }

    private static List<int> NormalizeSelectedCategoryIds(MessageReportTaskConfig config)
    {
        var ids = (config.CategoryIds ?? new List<int>())
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0 && config.CategoryId > 0)
            ids.Add(config.CategoryId);

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

    private static int SelectNextAccountIndex(List<AccountSlot> slots, ref int queueIndex)
    {
        if (slots.Count == 0)
            return -1;

        if (queueIndex < 0 || queueIndex >= slots.Count)
            queueIndex = 0;

        var selected = queueIndex;
        queueIndex = (queueIndex + 1) % slots.Count;
        return selected;
    }

    private static int SelectNextTargetIndex(AccountSlot slot, Dictionary<int, int> targetQueueIndexByAccountId)
    {
        if (slot.Targets.Count == 0)
            return -1;

        if (!targetQueueIndexByAccountId.TryGetValue(slot.Account.Id, out var index))
            index = 0;

        if (index < 0 || index >= slot.Targets.Count)
            index = 0;

        var selected = index;
        targetQueueIndexByAccountId[slot.Account.Id] = (selected + 1) % slot.Targets.Count;
        return selected;
    }

    private static int NextDelayMilliseconds(int min, int max)
    {
        if (min <= 0 && max <= 0)
            return 0;
        if (max <= min)
            return Math.Max(0, min);
        return Random.Shared.Next(min, max + 1);
    }

    private static bool ShouldPersistProgress(int completed, bool hadFailureThisRound, DateTime lastPersistAtUtc)
    {
        if (completed <= 0)
            return hadFailureThisRound;

        if (hadFailureThisRound)
            return true;

        if (completed % 5 == 0)
            return true;

        return (DateTime.UtcNow - lastPersistAtUtc) >= TimeSpan.FromSeconds(10);
    }

    private static async Task<bool> DelayWithTaskCheckAsync(IModuleTaskExecutionHost host, int delayMs, CancellationToken cancellationToken)
    {
        if (delayMs <= 0)
            return true;

        var remaining = delayMs;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await host.IsStillRunningAsync(cancellationToken))
                return false;

            var chunk = Math.Min(remaining, 1000);
            await Task.Delay(chunk, cancellationToken);
            remaining -= chunk;
        }

        return true;
    }

    private static void AddFailure(MessageReportTaskConfig config, Account account, string messageLink, string reason)
    {
        config.RecentFailures ??= new List<MessageReportTaskRuntimeFailure>();
        config.RecentFailures.Insert(0, new MessageReportTaskRuntimeFailure
        {
            TimeUtc = DateTime.UtcNow,
            AccountId = account.Id,
            Account = BuildAccountDisplayName(account),
            MessageLink = (messageLink ?? string.Empty).Trim(),
            Reason = reason
        });

        if (config.RecentFailures.Count > MaxFailureLines)
            config.RecentFailures = config.RecentFailures.Take(MaxFailureLines).ToList();
    }

    private static string BuildAccountDisplayName(Account account)
    {
        var username = string.IsNullOrWhiteSpace(account.Username) ? string.Empty : $"@{account.Username}";
        var nickname = string.IsNullOrWhiteSpace(account.Nickname) ? string.Empty : account.Nickname.Trim();
        return string.Join(" / ", new[] { account.DisplayPhone, username, nickname }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string NormalizeReason(string? reason)
    {
        var text = (reason ?? string.Empty).Trim();
        return text.Length == 0 ? "未知错误" : text;
    }

    private static string SerializeIndented(MessageReportTaskConfig config)
        => JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });

    private sealed class AccountSlot
    {
        public AccountSlot(Account account)
        {
            Account = account;
        }

        public Account Account { get; }
        public List<TargetSlot> Targets { get; } = new();
    }

    private sealed class TargetSlot
    {
        public TargetSlot(TelegramMessageReportService.ResolvedReportMessageTarget target)
        {
            Target = target;
        }

        public TelegramMessageReportService.ResolvedReportMessageTarget Target { get; }
        public int ConsecutiveFailures { get; set; }
    }
}
