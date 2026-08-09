using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.BatchTasks;
using TelegramPanel.Core.Services;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Core.Models;
using TelegramPanel.Data.Entities;
using TelegramPanel.Modules;

namespace TelegramPanel.Web.Services;

public sealed class AutoChangeLoginEmailTaskHandler : IModuleTaskHandler
{
    private static readonly JsonSerializerOptions PersistedConfigJsonOptions = new() { WriteIndented = true };

    public string TaskType => BatchTaskTypes.AutoChangeLoginEmail;

    public async Task ExecuteAsync(IModuleTaskExecutionHost host, CancellationToken cancellationToken)
    {
        var config = Deserialize(host.Config);
        Normalize(config);
        config.Items.Clear();
        config.RequestedAtUtc = DateTime.UtcNow;

        var accountManagement = host.Services.GetRequiredService<AccountManagementService>();
        var accountTools = host.Services.GetRequiredService<AccountTelegramToolsService>();
        var emailCode = host.Services.GetRequiredService<ITelegramEmailCodeService>();
        var taskManagement = host.Services.GetRequiredService<BatchTaskManagementService>();
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var logger = host.Services.GetRequiredService<ILogger<AutoChangeLoginEmailTaskHandler>>();

        var selectedCategoryIds = config.CategoryIds.Where(x => x > 0).ToHashSet();
        var accounts = (await accountManagement.GetActiveAccountsAsync())
            .Where(x => x.UserId > 0)
            .Where(x => x.CategoryId.HasValue && selectedCategoryIds.Contains(x.CategoryId.Value))
            .Where(x => x.Category?.ExcludeFromOperations != true)
            .OrderBy(x => x.Id)
            .ToList();
        if (accounts.Count == 0)
            throw new InvalidOperationException("所选账号分类下没有可用账号");

        var domain = ResolveDomain(config, configuration);
        var cloudMailConfigured = IsCloudMailConfigured(configuration) && !string.IsNullOrWhiteSpace(domain);
        var completed = 0;
        var failed = 0;

        foreach (var account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await host.IsStillRunningAsync(cancellationToken))
                return;

            var item = await ProcessAccountAsync(
                account,
                config,
                domain,
                cloudMailConfigured,
                accountTools,
                emailCode,
                logger,
                cancellationToken);

            config.Items.Add(item);
            if (config.Items.Count > 300)
                config.Items.RemoveRange(0, config.Items.Count - 300);

            completed++;
            if (string.Equals(item.Result, AutoChangeLoginEmailTaskResult.Failed, StringComparison.OrdinalIgnoreCase))
                failed++;

            await taskManagement.UpdateTaskConfigAsync(host.TaskId, Serialize(config));
            await host.UpdateProgressAsync(completed, failed, cancellationToken);
        }

        await taskManagement.UpdateTaskDraftAsync(host.TaskId, completed, Serialize(config));
    }

    private static async Task<AutoChangeLoginEmailTaskItem> ProcessAccountAsync(
        Account account,
        AutoChangeLoginEmailTaskConfig config,
        string domain,
        bool cloudMailConfigured,
        AccountTelegramToolsService accountTools,
        ITelegramEmailCodeService emailCode,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var targetEmail = BuildEmailByPhone(account.Phone, domain);
        IReadOnlyList<TelegramSystemMessage> messages = Array.Empty<TelegramSystemMessage>();
        var nowUtc = DateTimeOffset.UtcNow;

        try
        {
            if (!config.Force && cloudMailConfigured && !string.IsNullOrWhiteSpace(targetEmail))
            {
                var (fromUtc, toUtc) = AutoChangeLoginEmailNoticeDetector.BuildWindowUtc(
                    nowUtc,
                    config.TriggerDaysAgo,
                    config.TriggerWindowHours);
                messages = await accountTools.GetSystemMessagesInWindowAsync(
                    account.Id,
                    fromUtc.UtcDateTime,
                    toUtc.UtcDateTime,
                    config.MaxSystemMessages,
                    cancellationToken);
            }

            var decision = AutoChangeLoginEmailNoticeDetector.Decide(
                cloudMailConfigured,
                targetEmail,
                config.Force,
                messages,
                nowUtc,
                config.TriggerDaysAgo,
                config.TriggerWindowHours,
                config.TriggerPhrases);
            if (!decision.ShouldAttempt)
                return BuildItem(account, targetEmail, decision.Result, decision.Message, decision.Match);

            var (statusOk, statusError, hasLoginEmail, loginEmailPattern) = await accountTools.GetLoginEmailStatusAsync(account.Id, cancellationToken);
            if (!statusOk)
                return BuildItem(account, targetEmail, AutoChangeLoginEmailTaskResult.Failed, $"登录邮箱失败：获取状态失败：{statusError}", decision.Match);

            var startedAtUtc = DateTimeOffset.UtcNow;
            var (sent, sendError, pattern) = await accountTools.SetLoginEmailAsync(account.Id, targetEmail!, cancellationToken);
            if (!sent)
            {
                var message = sendError ?? "未知错误";
                if (message.Contains("EMAIL_NOT_SETUP", StringComparison.OrdinalIgnoreCase))
                {
                    return BuildItem(
                        account,
                        targetEmail,
                        AutoChangeLoginEmailTaskResult.Skipped,
                        $"登录邮箱不支持：该账号当前{(hasLoginEmail ? $"登录邮箱掩码 {loginEmailPattern}" : "未启用登录邮箱")}；{message}",
                        decision.Match);
                }

                return BuildItem(account, targetEmail, AutoChangeLoginEmailTaskResult.Failed, $"登录邮箱失败：{message}", decision.Match);
            }

            if (!config.AutoConfirm)
            {
                return BuildItem(
                    account,
                    targetEmail,
                    AutoChangeLoginEmailTaskResult.Success,
                    $"登录邮箱已发送验证码（未自动确认）{(string.IsNullOrWhiteSpace(pattern) ? string.Empty : $"，掩码：{pattern}")}",
                    decision.Match);
            }

            var code = await WaitEmailCodeAsync(
                emailCode,
                targetEmail!,
                startedAtUtc,
                config.PollIntervalSeconds,
                config.PollTimeoutSeconds,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(code))
            {
                return BuildItem(
                    account,
                    targetEmail,
                    AutoChangeLoginEmailTaskResult.Failed,
                    $"登录邮箱收码超时（{config.PollTimeoutSeconds}s）",
                    decision.Match);
            }

            var (confirmed, confirmError) = await accountTools.ConfirmLoginEmailAsync(account.Id, code, cancellationToken);
            if (!confirmed)
                return BuildItem(account, targetEmail, AutoChangeLoginEmailTaskResult.Failed, $"登录邮箱确认失败：{confirmError}", decision.Match);

            return BuildItem(account, targetEmail, AutoChangeLoginEmailTaskResult.Success, $"{decision.Message}；登录邮箱已确认", decision.Match);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "自动更改登录邮箱失败，账号 {AccountId}", account.Id);
            return BuildItem(account, targetEmail, AutoChangeLoginEmailTaskResult.Failed, $"登录邮箱失败：{ex.Message}", null);
        }
    }

    private static async Task<string?> WaitEmailCodeAsync(
        ITelegramEmailCodeService emailCode,
        string targetEmail,
        DateTimeOffset startedAtUtc,
        int pollIntervalSeconds,
        int pollTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(pollTimeoutSeconds);
        while (DateTimeOffset.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await emailCode.TryGetLatestCodeByEmailAsync(targetEmail, startedAtUtc, cancellationToken);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Code))
                return result.Code.Trim();

            var delay = TimeSpan.FromSeconds(Math.Clamp(pollIntervalSeconds, 2, 30));
            if (DateTimeOffset.UtcNow + delay > deadline)
                delay = deadline - DateTimeOffset.UtcNow;
            if (delay <= TimeSpan.Zero)
                break;
            await Task.Delay(delay, cancellationToken);
        }

        return null;
    }

    private static AutoChangeLoginEmailTaskItem BuildItem(
        Account account,
        string? targetEmail,
        string result,
        string message,
        AutoChangeLoginEmailNoticeMatch? match)
    {
        return new AutoChangeLoginEmailTaskItem
        {
            TimeUtc = DateTime.UtcNow,
            AccountId = account.Id,
            Phone = account.Phone,
            Email = targetEmail,
            Result = result,
            Message = message,
            MatchedMessageId = match?.MessageId,
            MatchedMessageDateUtc = match?.DateUtc
        };
    }

    private static bool IsCloudMailConfigured(IConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration["CloudMail:BaseUrl"])
            && !string.IsNullOrWhiteSpace(configuration["CloudMail:Token"]);
    }

    private static string ResolveDomain(AutoChangeLoginEmailTaskConfig config, IConfiguration configuration)
    {
        var domain = (config.Domain ?? string.Empty).Trim().TrimStart('@');
        if (domain.Length == 0)
            domain = (configuration["CloudMail:Domain"] ?? string.Empty).Trim().TrimStart('@');
        return domain;
    }

    internal static string BuildEmailByPhone(string? phone, string? domain)
    {
        domain = (domain ?? string.Empty).Trim().TrimStart('@');
        if (domain.Length == 0)
            return string.Empty;

        var digits = NormalizeDigits(phone);
        return digits.Length == 0 ? string.Empty : $"{digits}@{domain}";
    }

    private static string NormalizeDigits(string? value)
    {
        value ??= string.Empty;
        Span<char> buffer = value.Length <= 128 ? stackalloc char[value.Length] : new char[value.Length];
        var count = 0;
        foreach (var ch in value)
        {
            if (ch is >= '0' and <= '9')
                buffer[count++] = ch;
        }

        return count == 0 ? string.Empty : new string(buffer[..count]);
    }

    private static AutoChangeLoginEmailTaskConfig Deserialize(string? rawConfig)
    {
        var raw = (rawConfig ?? string.Empty).Trim();
        if (raw.Length == 0)
            return new AutoChangeLoginEmailTaskConfig();

        return JsonSerializer.Deserialize<AutoChangeLoginEmailTaskConfig>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new AutoChangeLoginEmailTaskConfig();
    }

    private static void Normalize(AutoChangeLoginEmailTaskConfig config)
    {
        config.CategoryIds = config.CategoryIds.Where(x => x > 0).Distinct().ToList();
        config.CategoryNames = config.CategoryNames.Select(x => (x ?? string.Empty).Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        config.Domain = (config.Domain ?? string.Empty).Trim().TrimStart('@');
        config.TriggerDaysAgo = Math.Clamp(config.TriggerDaysAgo, 0, 30);
        config.TriggerWindowHours = Math.Clamp(config.TriggerWindowHours, 1, 24 * 14);
        config.MaxSystemMessages = Math.Clamp(config.MaxSystemMessages, 20, 1000);
        config.PollIntervalSeconds = Math.Clamp(config.PollIntervalSeconds, 2, 30);
        config.PollTimeoutSeconds = Math.Clamp(config.PollTimeoutSeconds, 10, 600);
        config.TriggerPhrases = config.TriggerPhrases.Select(x => (x ?? string.Empty).Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        config.Items ??= new List<AutoChangeLoginEmailTaskItem>();
    }

    private static string Serialize(AutoChangeLoginEmailTaskConfig config) =>
        JsonSerializer.Serialize(config, PersistedConfigJsonOptions);
}
