using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.BatchTasks;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Services;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Data.Entities;
using TelegramPanel.Modules;

namespace TelegramPanel.Web.Services;

public sealed class BotChannelBanTaskHandler : IModuleTaskHandler
{
    public string TaskType => BatchTaskTypes.BotChannelBanMembers;

    public async Task ExecuteAsync(IModuleTaskExecutionHost host, CancellationToken cancellationToken)
    {
        var logger = host.Services.GetRequiredService<ILogger<BotChannelBanTaskHandler>>();
        var taskManagement = host.Services.GetRequiredService<BatchTaskManagementService>();
        var accountManagement = host.Services.GetRequiredService<AccountManagementService>();
        var channelService = host.Services.GetRequiredService<IChannelService>();
        var botTelegram = host.Services.GetRequiredService<BotTelegramService>();

        var config = DeserializeConfig(host.Config);
        ValidateConfig(config);

        var channels = config.Channels
            .Where(x => x != null && x.TelegramId != 0)
            .GroupBy(x => x.TelegramId)
            .Select(x => x.First())
            .ToList();

        var channelTitleById = channels.ToDictionary(x => x.TelegramId, NormalizeChannelTitle);
        var username = NormalizeUsername(config.Username);

        var completed = 0;
        var failed = 0;
        var failuresByChannel = new Dictionary<long, BotAdminTaskFailureItem>();

        try
        {
            if (config.UseAccountExecution)
            {
                await ExecuteByAccountAsync(
                    host,
                    config,
                    channels,
                    username,
                    channelTitleById,
                    accountManagement,
                    botTelegram,
                    channelService,
                    failuresByChannel,
                    onProgress: async (done, fail) =>
                    {
                        completed = done;
                        failed = fail;
                        await host.UpdateProgressAsync(completed, failed, cancellationToken);
                    },
                    cancellationToken);
            }
            else
            {
                await ExecuteByBotAsync(
                    host,
                    config,
                    channels,
                    channelTitleById,
                    botTelegram,
                    failuresByChannel,
                    onProgress: async (done, fail) =>
                    {
                        completed = done;
                        failed = fail;
                        await host.UpdateProgressAsync(completed, failed, cancellationToken);
                    },
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BotChannelBan task failed (taskId={TaskId})", host.TaskId);
            config.Error = ex.Message;
            config.Failures = failuresByChannel.Values
                .OrderBy(x => x.ChannelTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ChannelTelegramId)
                .ToList();
            config.FailureLines = BotAdminTaskFailureFormatter.BuildLines(config.Failures);
            await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));
            throw;
        }

        config.Failures = failuresByChannel.Values
            .OrderBy(x => x.ChannelTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ChannelTelegramId)
            .ToList();
        config.FailureLines = BotAdminTaskFailureFormatter.BuildLines(config.Failures);
        await taskManagement.UpdateTaskConfigAsync(host.TaskId, SerializeIndented(config));
    }

    private static async Task ExecuteByBotAsync(
        IModuleTaskExecutionHost host,
        BotChannelBanTaskConfig config,
        IReadOnlyList<BotTaskChannelItem> channels,
        IReadOnlyDictionary<long, string> channelTitleById,
        BotTelegramService botTelegram,
        IDictionary<long, BotAdminTaskFailureItem> failuresByChannel,
        Func<int, int, Task> onProgress,
        CancellationToken cancellationToken)
    {
        if (config.UserId is null or <= 0)
            throw new InvalidOperationException("机器人执行仅支持用户 ID（纯数字）");

        if (!await host.IsStillRunningAsync(cancellationToken))
        {
            config.Canceled = true;
            return;
        }

        var chatIds = channels.Select(x => x.TelegramId).Distinct().ToList();
        var result = await botTelegram.BanChatMemberAsync(
            botId: config.BotId,
            channelTelegramIds: chatIds,
            userId: config.UserId.Value,
            permanentBan: config.PermanentBan,
            cancellationToken: cancellationToken,
            perChatCallback: async (chatId, reason, ok, fail) =>
            {
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    failuresByChannel[chatId] = new BotAdminTaskFailureItem
                    {
                        ChannelTelegramId = chatId,
                        ChannelTitle = channelTitleById.TryGetValue(chatId, out var title) ? title : chatId.ToString(),
                        UserId = config.UserId,
                        Reason = NormalizeReason(reason)
                    };
                }

                await onProgress(ok + fail, fail);
            });

        foreach (var (chatId, reason) in result.Failures)
        {
            failuresByChannel[chatId] = new BotAdminTaskFailureItem
            {
                ChannelTelegramId = chatId,
                ChannelTitle = channelTitleById.TryGetValue(chatId, out var title) ? title : chatId.ToString(),
                UserId = config.UserId,
                Reason = NormalizeReason(reason)
            };
        }

        await onProgress(result.SuccessCount + result.Failures.Count, result.Failures.Count);
    }

    private static async Task ExecuteByAccountAsync(
        IModuleTaskExecutionHost host,
        BotChannelBanTaskConfig config,
        IReadOnlyList<BotTaskChannelItem> channels,
        string? username,
        IReadOnlyDictionary<long, string> channelTitleById,
        AccountManagementService accountManagement,
        BotTelegramService botTelegram,
        IChannelService channelService,
        IDictionary<long, BotAdminTaskFailureItem> failuresByChannel,
        Func<int, int, Task> onProgress,
        CancellationToken cancellationToken)
    {
        var allAccounts = (await accountManagement.GetAllAccountsAsync())
            .Where(x => x.IsActive && x.Category?.ExcludeFromOperations != true && x.UserId > 0)
            .ToList();

        var accountsById = allAccounts.ToDictionary(x => x.Id);
        var accountsByUserId = new Dictionary<long, Account>();
        foreach (var account in allAccounts)
        {
            if (!accountsByUserId.ContainsKey(account.UserId))
                accountsByUserId[account.UserId] = account;
        }

        var channelAdmins = new Dictionary<long, List<BotTelegramService.BotChatAdminInfo>>();
        foreach (var channel in channels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await host.IsStillRunningAsync(cancellationToken))
            {
                config.Canceled = true;
                return;
            }

            try
            {
                channelAdmins[channel.TelegramId] = await botTelegram.GetChatAdminsAsync(
                    config.BotId,
                    channel.TelegramId,
                    cancellationToken);
            }
            catch
            {
                channelAdmins[channel.TelegramId] = new List<BotTelegramService.BotChatAdminInfo>();
            }
        }

        var completed = 0;
        var failed = 0;

        foreach (var channel in channels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await host.IsStillRunningAsync(cancellationToken))
            {
                config.Canceled = true;
                break;
            }

            var (executorId, reason) = ResolveExecutorAccountId(
                channel,
                config.SelectedAccountId,
                channelAdmins,
                accountsById,
                accountsByUserId);

            if (!executorId.HasValue)
            {
                failed++;
                completed++;
                failuresByChannel[channel.TelegramId] = new BotAdminTaskFailureItem
                {
                    ChannelTelegramId = channel.TelegramId,
                    ChannelTitle = channelTitleById.TryGetValue(channel.TelegramId, out var title) ? title : channel.TelegramId.ToString(),
                    Username = username,
                    UserId = config.UserId,
                    Reason = NormalizeReason(reason ?? "无可用执行账号")
                };

                await onProgress(completed, failed);
                continue;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(username))
                {
                    await channelService.KickUserAsync(
                        accountId: executorId.Value,
                        channelId: channel.TelegramId,
                        username: username,
                        permanentBan: config.PermanentBan);
                }
                else if (config.UserId is > 0)
                {
                    await channelService.KickUserByUserIdAsync(
                        accountId: executorId.Value,
                        channelId: channel.TelegramId,
                        userId: config.UserId.Value,
                        permanentBan: config.PermanentBan);
                }
                else
                {
                    throw new InvalidOperationException("账号执行需要 @username 或用户 ID");
                }
            }
            catch (Exception ex)
            {
                failed++;
                failuresByChannel[channel.TelegramId] = new BotAdminTaskFailureItem
                {
                    ChannelTelegramId = channel.TelegramId,
                    ChannelTitle = channelTitleById.TryGetValue(channel.TelegramId, out var title) ? title : channel.TelegramId.ToString(),
                    Username = username,
                    UserId = config.UserId,
                    Reason = NormalizeReason(ex.Message)
                };
            }
            finally
            {
                completed++;
                await onProgress(completed, failed);
            }

            var wait = config.DelayMs;
            if (wait < 0) wait = 0;
            if (wait > 30000) wait = 30000;
            var jitter = Random.Shared.Next(300, 1000);
            await Task.Delay(TimeSpan.FromMilliseconds(wait + jitter), cancellationToken);
        }
    }

    private static BotChannelBanTaskConfig DeserializeConfig(string? rawConfig)
    {
        var raw = (rawConfig ?? string.Empty).Trim();
        if (raw.Length == 0)
            throw new InvalidOperationException("任务缺少 Config");

        try
        {
            return JsonSerializer.Deserialize<BotChannelBanTaskConfig>(raw)
                   ?? throw new InvalidOperationException("任务 Config JSON 为空");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"任务 Config JSON 无效：{ex.Message}");
        }
    }

    private static void ValidateConfig(BotChannelBanTaskConfig config)
    {
        if (config.BotId <= 0)
            throw new InvalidOperationException("任务 BotId 无效");
        if (config.Channels.Count == 0)
            throw new InvalidOperationException("任务缺少频道列表");

        config.Username = NormalizeUsername(config.Username);

        if (config.UseAccountExecution)
        {
            if (string.IsNullOrWhiteSpace(config.Username) && (config.UserId is null or <= 0))
                throw new InvalidOperationException("账号执行需要 @username 或用户 ID");
        }
        else
        {
            if (config.UserId is null or <= 0)
                throw new InvalidOperationException("机器人执行仅支持用户 ID（纯数字）");
        }

        if (config.DelayMs < 0) config.DelayMs = 0;
        if (config.DelayMs > 30000) config.DelayMs = 30000;
    }

    private static (int? ExecutorId, string? Reason) ResolveExecutorAccountId(
        BotTaskChannelItem channel,
        int selectedAccountId,
        IReadOnlyDictionary<long, List<BotTelegramService.BotChatAdminInfo>> channelAdmins,
        IReadOnlyDictionary<int, Account> accountsById,
        IReadOnlyDictionary<long, Account> accountsByUserId)
    {
        if (!channelAdmins.TryGetValue(channel.TelegramId, out var admins) || admins.Count == 0)
            return (null, "无法获取频道管理员列表（请确认 Bot 已加入且为管理员）");

        if (selectedAccountId > 0)
        {
            if (!accountsById.TryGetValue(selectedAccountId, out var selected) || selected.UserId <= 0)
                return (null, "所选执行账号无效");

            var admin = admins.FirstOrDefault(x => x.UserId == selected.UserId);
            if (admin == null)
                return (null, "所选执行账号不是该频道管理员");

            if (!admin.IsCreator && !admin.CanRestrictMembers)
                return (null, "所选执行账号缺少“封禁用户”权限");

            return (selected.Id, null);
        }

        var creator = admins.FirstOrDefault(x => x.IsCreator);
        if (creator != null && accountsByUserId.TryGetValue(creator.UserId, out var creatorAcc))
            return (creatorAcc.Id, null);

        foreach (var admin in admins)
        {
            if (!admin.IsCreator && !admin.CanRestrictMembers)
                continue;

            if (accountsByUserId.TryGetValue(admin.UserId, out var account))
                return (account.Id, null);
        }

        return (null, "无可用执行账号（需要该频道管理员且拥有“封禁用户”权限，并且在系统中存在）");
    }

    private static string NormalizeReason(string? reason)
    {
        var text = (reason ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(text) ? "失败" : text;
    }

    private static string NormalizeChannelTitle(BotTaskChannelItem channel)
    {
        var title = (channel.Title ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(title) ? channel.TelegramId.ToString() : title;
    }

    private static string? NormalizeUsername(string? username)
    {
        var value = (username ?? string.Empty).Trim().TrimStart('@');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string SerializeIndented(BotChannelBanTaskConfig config)
    {
        return JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
