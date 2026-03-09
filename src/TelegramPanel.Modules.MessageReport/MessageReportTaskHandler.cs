using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Services;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Data.Entities;
using TelegramPanel.Modules;

namespace TelegramPanel.Modules.MessageReport;

public sealed class MessageReportTaskHandler : IModuleTaskHandler
{
    private const int MaxFailureLines = 20;

    public string TaskType => MessageReportTaskTypes.TaskType;

    public async Task ExecuteAsync(IModuleTaskExecutionHost host, CancellationToken cancellationToken)
    {
        var logger = host.Services.GetRequiredService<ILogger<MessageReportTaskHandler>>();
        var taskManagement = host.Services.GetRequiredService<BatchTaskManagementService>();
        var accountManagement = host.Services.GetRequiredService<AccountManagementService>();
        var telegramService = host.Services.GetRequiredService<MessageReportTaskTelegramService>();

        var config = DeserializeConfig(host.Config);
        ValidateAndNormalizeConfig(config);

        var selectedCategoryIds = MessageReportTaskInputHelper.NormalizeCategoryIds(config.CategoryIds, config.CategoryId).ToHashSet();
        var allAccounts = (await accountManagement.GetAllAccountsAsync())
            .Where(x => x.IsActive && x.UserId > 0 && x.Category?.ExcludeFromOperations != true)
            .Where(x => x.CategoryId.HasValue && selectedCategoryIds.Contains(x.CategoryId.Value))
            .OrderBy(x => x.Id)
            .ToList();

        if (allAccounts.Count == 0)
        {
            config.Error = "所选分类下没有可用执行账号";
            await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));
            throw new InvalidOperationException(config.Error);
        }

        var parsedMessageReferences = config.MessageReferences
            .Select(raw =>
            {
                if (!MessageReportTaskInputHelper.TryParseMessageReference(raw, out var parsed, out var error) || parsed == null)
                    throw new InvalidOperationException($"消息引用无效：{raw}（{error}）");
                return parsed;
            })
            .ToList();

        var completed = Math.Max(0, config.CompletedAttempts);
        var failed = Math.Max(0, config.FailedAttempts);
        var accountCursor = NormalizeCursor(config.AccountCursor, allAccounts.Count);
        var messageCursor = NormalizeCursor(config.MessageCursor, parsedMessageReferences.Count);
        var commentCursor = Math.Max(0, config.CommentCursor);
        var resolvedCache = new Dictionary<string, ResolvedMessageTarget>(StringComparer.OrdinalIgnoreCase);

        config.Canceled = false;
        config.Error = null;
        config.CompletedAttempts = completed;
        config.FailedAttempts = failed;
        config.AccountCursor = accountCursor;
        config.MessageCursor = messageCursor;
        config.CommentCursor = commentCursor;
        await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));
        await host.UpdateProgressAsync(completed, failed, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await host.IsStillRunningAsync(cancellationToken))
            {
                config.Canceled = true;
                config.Error = null;
                config.CompletedAttempts = completed;
                config.FailedAttempts = failed;
                config.AccountCursor = accountCursor;
                config.MessageCursor = messageCursor;
                config.CommentCursor = commentCursor;
                await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));
                return;
            }

            if (config.MaxReports > 0 && completed >= config.MaxReports)
                break;

            var account = allAccounts[SelectIndex(allAccounts.Count, ref accountCursor)];
            var messageReference = parsedMessageReferences[SelectIndex(parsedMessageReferences.Count, ref messageCursor)];
            var cacheKey = $"{account.Id}:{messageReference.CanonicalId}";

            string? failureReason = null;
            try
            {
                if (!resolvedCache.TryGetValue(cacheKey, out var resolvedTarget))
                {
                    var resolved = await telegramService.ResolveMessageReferenceAsync(account.Id, messageReference.Raw, cancellationToken);
                    if (!resolved.Success || resolved.Target == null)
                    {
                        failureReason = NormalizeReason(resolved.Error);
                    }
                    else
                    {
                        resolvedTarget = resolved.Target;
                        resolvedCache[cacheKey] = resolvedTarget;
                    }
                }

                if (failureReason == null && resolvedCache.TryGetValue(cacheKey, out var cachedTarget))
                {
                    var report = await telegramService.ReportMessageAsync(account.Id, cachedTarget, config, commentCursor, cancellationToken);
                    commentCursor = Math.Max(0, report.NextCommentCursor);
                    if (!string.IsNullOrWhiteSpace(report.SelectedOptionText))
                        logger.LogDebug("Message report option selected: {OptionText}", report.SelectedOptionText);
                    if (!report.Success)
                    {
                        failureReason = NormalizeReason(report.Error);
                        if (LooksLikePeerInvalid(report.Error))
                            resolvedCache.Remove(cacheKey);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Message report task iteration failed (taskId={TaskId}, accountId={AccountId}, message={Message})", host.TaskId, account.Id, messageReference.CanonicalId);
                var mapped = AccountTelegramToolsService.MapTelegramException(ex);
                failureReason = NormalizeReason(string.IsNullOrWhiteSpace(mapped.details) ? mapped.summary : $"{mapped.summary}：{mapped.details}");
            }

            completed++;
            if (!string.IsNullOrWhiteSpace(failureReason))
            {
                failed++;
                AddFailure(config, account, messageReference.Raw, failureReason!);
            }

            config.Canceled = false;
            config.Error = null;
            config.CompletedAttempts = completed;
            config.FailedAttempts = failed;
            config.AccountCursor = accountCursor;
            config.MessageCursor = messageCursor;
            config.CommentCursor = commentCursor;
            await host.UpdateProgressAsync(completed, failed, cancellationToken);
            await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));

            if (config.MaxReports > 0 && completed >= config.MaxReports)
                break;

            if (!await DelayAsync(host, config.DelayMinMs, config.DelayMaxMs, cancellationToken))
            {
                config.Canceled = true;
                config.Error = null;
                config.CompletedAttempts = completed;
                config.FailedAttempts = failed;
                config.AccountCursor = accountCursor;
                config.MessageCursor = messageCursor;
                config.CommentCursor = commentCursor;
                await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));
                return;
            }
        }

        config.Canceled = false;
        config.Error = null;
        config.CompletedAttempts = completed;
        config.FailedAttempts = failed;
        config.AccountCursor = accountCursor;
        config.MessageCursor = messageCursor;
        config.CommentCursor = commentCursor;
        await host.UpdateProgressAsync(completed, failed, cancellationToken);
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
        config.CategoryIds = MessageReportTaskInputHelper.NormalizeCategoryIds(config.CategoryIds, config.CategoryId);
        config.CategoryNames = MessageReportTaskInputHelper.NormalizeCategoryNames(config.CategoryNames, config.CategoryName);
        config.MessageReferences = MessageReportTaskInputHelper.NormalizeLines(config.MessageReferences, distinct: true);
        config.OptionKeywords = MessageReportTaskInputHelper.NormalizeLines(config.OptionKeywords, distinct: true);
        config.Comments = MessageReportTaskInputHelper.NormalizeLines(config.Comments);
        config.CommentMode = MessageReportTaskModes.Normalize(config.CommentMode);
        config.ReportType = MessageReportTaskReportTypes.Normalize(config.ReportType);
        config.DelayMinMs = Math.Clamp(config.DelayMinMs, 0, 600000);
        config.DelayMaxMs = Math.Clamp(config.DelayMaxMs, config.DelayMinMs, 600000);
        config.MaxReports = Math.Max(0, config.MaxReports);
        config.CompletedAttempts = Math.Max(0, config.CompletedAttempts);
        config.FailedAttempts = Math.Max(0, config.FailedAttempts);
        config.AccountCursor = Math.Max(0, config.AccountCursor);
        config.MessageCursor = Math.Max(0, config.MessageCursor);
        config.CommentCursor = Math.Max(0, config.CommentCursor);
        config.RecentFailures ??= new List<MessageReportTaskRuntimeFailure>();

        if (config.CategoryIds.Count == 0)
            throw new InvalidOperationException("请至少选择一个执行账号分类");

        if (config.MessageReferences.Count == 0)
            throw new InvalidOperationException("请至少填写一个消息引用");

        foreach (var messageReference in config.MessageReferences)
        {
            if (!MessageReportTaskInputHelper.TryParseMessageReference(messageReference, out _, out var error))
                throw new InvalidOperationException($"消息引用无效：{messageReference}（{error}）");
        }
    }

    private static int SelectIndex(int count, ref int cursor)
    {
        if (count <= 0)
            return 0;

        var index = NormalizeCursor(cursor, count);
        cursor = (index + 1) % count;
        return index;
    }

    private static int NormalizeCursor(int cursor, int count)
    {
        if (count <= 0)
            return 0;
        if (cursor < 0)
            return 0;
        return cursor % count;
    }

    private static async Task<bool> DelayAsync(IModuleTaskExecutionHost host, int minDelayMs, int maxDelayMs, CancellationToken cancellationToken)
    {
        var delayMs = NextDelayMilliseconds(minDelayMs, maxDelayMs);
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

    private static int NextDelayMilliseconds(int minDelayMs, int maxDelayMs)
    {
        minDelayMs = Math.Max(0, minDelayMs);
        maxDelayMs = Math.Max(minDelayMs, maxDelayMs);
        return maxDelayMs <= minDelayMs
            ? minDelayMs
            : Random.Shared.Next(minDelayMs, maxDelayMs + 1);
    }

    private static void AddFailure(MessageReportTaskConfig config, Account account, string messageReference, string reason)
    {
        config.RecentFailures ??= new List<MessageReportTaskRuntimeFailure>();
        config.RecentFailures.Add(new MessageReportTaskRuntimeFailure
        {
            TimestampUtc = DateTime.UtcNow,
            AccountId = account.Id,
            AccountDisplay = BuildAccountDisplayName(account),
            MessageReference = messageReference,
            Reason = reason
        });

        if (config.RecentFailures.Count > MaxFailureLines)
            config.RecentFailures = config.RecentFailures.TakeLast(MaxFailureLines).ToList();
    }

    private static string BuildAccountDisplayName(Account account)
    {
        return $"#{account.Id} {(string.IsNullOrWhiteSpace(account.DisplayPhone) ? account.Phone : account.DisplayPhone)}";
    }

    private static bool LooksLikePeerInvalid(string? error)
    {
        var text = (error ?? string.Empty).ToUpperInvariant();
        return text.Contains("PEER_ID_INVALID", StringComparison.Ordinal)
               || text.Contains("CHAT_ID_INVALID", StringComparison.Ordinal)
               || text.Contains("CHANNEL_INVALID", StringComparison.Ordinal)
               || text.Contains("USERNAME_INVALID", StringComparison.Ordinal)
               || text.Contains("USERNAME_NOT_OCCUPIED", StringComparison.Ordinal);
    }

    private static string NormalizeReason(string? error)
    {
        return string.IsNullOrWhiteSpace(error) ? "Telegram 请求失败" : error.Trim();
    }

    private static string SerializeIndented(MessageReportTaskConfig config)
    {
        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    }
}
