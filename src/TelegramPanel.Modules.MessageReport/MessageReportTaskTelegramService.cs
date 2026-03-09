using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Services;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Data.Entities;
using TL;
using WTelegram;

namespace TelegramPanel.Modules.MessageReport;

public sealed class MessageReportTaskTelegramService
{
    private static readonly IReadOnlyDictionary<string, string[]> ReportTypeKeywords = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [MessageReportTaskReportTypes.Spam] = ["spam", "垃圾", "广告"],
        [MessageReportTaskReportTypes.Violence] = ["violence", "violent", "暴力"],
        [MessageReportTaskReportTypes.Pornography] = ["porn", "pornography", "sexual", "nsfw", "色情", "成人"],
        [MessageReportTaskReportTypes.ChildAbuse] = ["child", "minor", "underage", "儿童", "未成年", "虐待", "剥削"],
        [MessageReportTaskReportTypes.Copyright] = ["copyright", "版权", "侵权", "盗版"],
        [MessageReportTaskReportTypes.PersonalDetails] = ["personal", "private", "privacy", "隐私", "个人信息", "泄露"],
        [MessageReportTaskReportTypes.IllegalDrugs] = ["drug", "drugs", "毒品", "违禁药"],
        [MessageReportTaskReportTypes.Fake] = ["fake", "fraud", "scam", "冒充", "诈骗", "虚假"],
        [MessageReportTaskReportTypes.Other] = ["other", "其他"]
    };

    private readonly AccountManagementService _accountManagement;
    private readonly ITelegramClientPool _clientPool;
    private readonly AccountTelegramToolsService _accountTools;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MessageReportTaskTelegramService> _logger;

    public MessageReportTaskTelegramService(
        AccountManagementService accountManagement,
        ITelegramClientPool clientPool,
        AccountTelegramToolsService accountTools,
        IConfiguration configuration,
        ILogger<MessageReportTaskTelegramService> logger)
    {
        _accountManagement = accountManagement;
        _clientPool = clientPool;
        _accountTools = accountTools;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(bool Success, string? Error, ResolvedMessageTarget? Target)> ResolveMessageReferenceAsync(
        int accountId,
        string rawReference,
        CancellationToken cancellationToken)
    {
        if (!MessageReportTaskInputHelper.TryParseMessageReference(rawReference, out var parsed, out var parseError) || parsed == null)
            return (false, parseError ?? "消息引用无效", null);

        var resolved = await _accountTools.ResolveChatTargetAsync(accountId, parsed.Target, cancellationToken);
        if (!resolved.Success || resolved.Target == null)
            return (false, NormalizeError(resolved.Error), null);

        return (true, null, new ResolvedMessageTarget(parsed, resolved.Target));
    }

    public async Task<ReportMessageResult> ReportMessageAsync(
        int accountId,
        ResolvedMessageTarget target,
        MessageReportTaskConfig config,
        int commentCursor,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = await GetOrCreateConnectedClientAsync(accountId, cancellationToken);
            var option = Array.Empty<byte>();
            var comment = string.Empty;
            string? selectedOptionText = null;
            var nextCommentCursor = commentCursor;

            for (var step = 0; step < 6; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await ExecuteTelegramRequestAsync(
                    accountId,
                    "举报消息",
                    () => client.Messages_Report(target.ChatTarget.Peer, [target.Reference.MessageId], option, comment),
                    cancellationToken,
                    resetClientOnTimeout: true);

                switch (result)
                {
                    case ReportResultReported:
                        return ReportMessageResult.Ok(nextCommentCursor, selectedOptionText);

                    case ReportResultChooseOption chooseOption:
                    {
                        var selected = SelectOption(chooseOption.options, config.ReportType, config.OptionKeywords, out var matchError);
                        if (selected == null)
                            return ReportMessageResult.Fail(matchError ?? "无法匹配 Telegram 返回的举报菜单", nextCommentCursor);

                        option = selected.option ?? Array.Empty<byte>();
                        comment = string.Empty;
                        selectedOptionText = selected.text;
                        break;
                    }

                    case ReportResultAddComment addComment:
                    {
                        option = addComment.option ?? option;
                        var selectedComment = PickComment(config.Comments, config.CommentMode, nextCommentCursor, out nextCommentCursor);
                        if (string.IsNullOrWhiteSpace(selectedComment))
                        {
                            if (!addComment.flags.HasFlag(ReportResultAddComment.Flags.optional))
                                return ReportMessageResult.Fail("当前举报步骤要求填写举报说明，但任务配置未提供可用文案", nextCommentCursor);

                            comment = string.Empty;
                        }
                        else
                        {
                            comment = selectedComment;
                        }

                        break;
                    }

                    default:
                        return ReportMessageResult.Fail($"收到未知举报结果类型：{result.GetType().Name}", nextCommentCursor);
                }
            }

            return ReportMessageResult.Fail("举报流程超过允许的最大步骤数", commentCursor);
        }
        catch (Exception ex)
        {
            var mapped = AccountTelegramToolsService.MapTelegramException(ex);
            _logger.LogWarning(ex, "Message report failed for account {AccountId}, target {Target}", accountId, target.Reference.CanonicalId);
            return ReportMessageResult.Fail(string.IsNullOrWhiteSpace(mapped.details) ? mapped.summary : $"{mapped.summary}：{mapped.details}", commentCursor);
        }
    }

    private async Task<Client> GetOrCreateConnectedClientAsync(int accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = _clientPool.GetClient(accountId);
        if (existing?.User != null)
            return existing;

        var account = await _accountManagement.GetAccountAsync(accountId)
            ?? throw new InvalidOperationException($"账号不存在：{accountId}");

        var apiId = ResolveApiId(account);
        var apiHash = ResolveApiHash(account);
        var sessionKey = ResolveSessionKey(account, apiHash);

        if (string.IsNullOrWhiteSpace(account.SessionPath))
            throw new InvalidOperationException("账号缺少 SessionPath，无法创建 Telegram 客户端");

        var absoluteSessionPath = Path.GetFullPath(account.SessionPath);
        if (File.Exists(absoluteSessionPath) && SessionDataConverter.LooksLikeSqliteSession(absoluteSessionPath))
        {
            var converted = await SessionDataConverter.TryConvertSqliteSessionFromJsonAsync(
                phone: account.Phone,
                apiId: account.ApiId,
                apiHash: account.ApiHash,
                sqliteSessionPath: absoluteSessionPath,
                logger: _logger);

            if (!converted.Ok)
            {
                throw new InvalidOperationException(
                    $"该账号的 Session 文件为 SQLite 格式：{account.SessionPath}，无法自动转换为可用 session。原因：{converted.Reason}。"
                    + "建议：重新导入包含 session_string 的 json，或到【账号-手机号登录】重新登录生成新的 sessions/*.session。"
                );
            }
        }

        await _clientPool.RemoveClientAsync(accountId);

        var client = await _clientPool.GetOrCreateClientAsync(
            accountId: accountId,
            apiId: apiId,
            apiHash: apiHash,
            sessionPath: account.SessionPath,
            sessionKey: sessionKey,
            phoneNumber: account.Phone,
            userId: account.UserId > 0 ? account.UserId : null);

        try
        {
            await ExecuteTelegramRequestAsync(
                accountId,
                "连接 Telegram",
                () => client.ConnectAsync(),
                cancellationToken,
                resetClientOnTimeout: true);

            if (client.User == null && (client.UserId != 0 || account.UserId != 0))
            {
                await ExecuteTelegramRequestAsync(
                    accountId,
                    "恢复 Telegram 登录状态",
                    () => client.LoginUserIfNeeded(reloginOnFailedResume: false),
                    cancellationToken,
                    resetClientOnTimeout: true);
            }
        }
        catch (Exception ex)
        {
            if (LooksLikeSessionApiMismatchOrCorrupted(ex))
            {
                throw new InvalidOperationException(
                    "该账号的 Session 文件无法解析（通常是 ApiId/ApiHash 与生成 session 时不一致，或 session 文件已损坏）。"
                    + "请到【账号-手机号登录】重新登录生成新的 session 后再试。",
                    ex);
            }

            throw new InvalidOperationException($"Telegram 会话加载失败：{ex.Message}", ex);
        }

        if (client.User == null)
            throw new InvalidOperationException("账号未登录或 session 已失效，请重新登录生成新的 session");

        return client;
    }

    private async Task ExecuteTelegramRequestAsync(
        int accountId,
        string operation,
        Func<Task> action,
        CancellationToken cancellationToken,
        bool resetClientOnTimeout = false)
    {
        try
        {
            await action().WaitAsync(GetTelegramRequestTimeout(), cancellationToken);
        }
        catch (TimeoutException)
        {
            if (resetClientOnTimeout)
                await _clientPool.RemoveClientAsync(accountId);

            throw new TimeoutException($"{operation} 超时");
        }
    }

    private async Task<T> ExecuteTelegramRequestAsync<T>(
        int accountId,
        string operation,
        Func<Task<T>> action,
        CancellationToken cancellationToken,
        bool resetClientOnTimeout = false)
    {
        try
        {
            return await action().WaitAsync(GetTelegramRequestTimeout(), cancellationToken);
        }
        catch (TimeoutException)
        {
            if (resetClientOnTimeout)
                await _clientPool.RemoveClientAsync(accountId);

            throw new TimeoutException($"{operation} 超时");
        }
    }

    private TimeSpan GetTelegramRequestTimeout()
    {
        if (int.TryParse(_configuration["Telegram:RequestTimeoutSeconds"], out var seconds) && seconds >= 10 && seconds <= 600)
            return TimeSpan.FromSeconds(seconds);

        return TimeSpan.FromSeconds(45);
    }

    private static MessageReportOption? SelectOption(
        IEnumerable<MessageReportOption>? options,
        string reportType,
        IEnumerable<string>? customKeywords,
        out string? error)
    {
        error = null;
        var optionList = (options ?? Array.Empty<MessageReportOption>()).ToList();
        if (optionList.Count == 0)
        {
            error = "Telegram 未返回可用的举报选项";
            return null;
        }

        var keywords = BuildKeywords(reportType, customKeywords).ToList();
        if (keywords.Count > 0)
        {
            foreach (var candidate in optionList)
            {
                var normalizedText = NormalizeForMatch(candidate.text);
                if (keywords.Any(keyword => normalizedText.Contains(keyword, StringComparison.Ordinal)))
                    return candidate;
            }
        }

        if (string.Equals(reportType, MessageReportTaskReportTypes.Auto, StringComparison.OrdinalIgnoreCase))
            return optionList[0];

        if (optionList.Count == 1)
            return optionList[0];

        error = "未能根据当前配置匹配 Telegram 返回的举报选项："
                + string.Join(" / ", optionList.Select(x => string.IsNullOrWhiteSpace(x.text) ? "<空标题>" : x.text.Trim()));
        return null;
    }

    private static IEnumerable<string> BuildKeywords(string reportType, IEnumerable<string>? customKeywords)
    {
        var normalizedReportType = MessageReportTaskReportTypes.Normalize(reportType);
        if (ReportTypeKeywords.TryGetValue(normalizedReportType, out var defaultKeywords))
        {
            foreach (var keyword in defaultKeywords)
            {
                var normalized = NormalizeForMatch(keyword);
                if (normalized.Length > 0)
                    yield return normalized;
            }
        }

        foreach (var keyword in MessageReportTaskInputHelper.NormalizeLines(customKeywords, distinct: true))
        {
            var normalized = NormalizeForMatch(keyword);
            if (normalized.Length > 0)
                yield return normalized;
        }
    }

    private static string NormalizeForMatch(string? value)
    {
        return new string((value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Where(ch => !char.IsWhiteSpace(ch) && ch is not '-' and not '_' and not ':' and not '：')
            .ToArray());
    }

    private static string? PickComment(IReadOnlyList<string>? comments, string mode, int cursor, out int nextCursor)
    {
        var normalized = MessageReportTaskInputHelper.NormalizeLines(comments, distinct: false);
        if (normalized.Count == 0)
        {
            nextCursor = cursor;
            return null;
        }

        if (string.Equals(MessageReportTaskModes.Normalize(mode), MessageReportTaskModes.Queue, StringComparison.OrdinalIgnoreCase))
        {
            var index = NormalizeCursor(cursor, normalized.Count);
            nextCursor = normalized.Count == 0 ? 0 : (index + 1) % normalized.Count;
            return normalized[index];
        }

        nextCursor = cursor;
        return normalized[Random.Shared.Next(normalized.Count)];
    }

    private static int NormalizeCursor(int cursor, int count)
    {
        if (count <= 0)
            return 0;

        if (cursor < 0)
            return 0;

        return cursor % count;
    }

    private static string NormalizeError(string? error)
    {
        return string.IsNullOrWhiteSpace(error) ? "Telegram 请求失败" : error.Trim();
    }

    private int ResolveApiId(Account account)
    {
        if (int.TryParse(_configuration["Telegram:ApiId"], out var globalApiId) && globalApiId > 0)
            return globalApiId;
        if (account.ApiId > 0)
            return account.ApiId;
        throw new InvalidOperationException("未配置全局 ApiId，且账号缺少 ApiId");
    }

    private string ResolveApiHash(Account account)
    {
        var global = _configuration["Telegram:ApiHash"];
        if (!string.IsNullOrWhiteSpace(global))
            return global.Trim();
        if (!string.IsNullOrWhiteSpace(account.ApiHash))
            return account.ApiHash.Trim();
        throw new InvalidOperationException("未配置全局 ApiHash，且账号缺少 ApiHash");
    }

    private static string ResolveSessionKey(Account account, string apiHash)
    {
        return !string.IsNullOrWhiteSpace(account.ApiHash) ? account.ApiHash.Trim() : apiHash.Trim();
    }

    private static bool LooksLikeSessionApiMismatchOrCorrupted(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("Can't read session block", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Use the correct api_hash", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Use the correct api_hash/id/key", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record ResolvedMessageTarget(
    ParsedMessageReference Reference,
    AccountTelegramToolsService.ResolvedChatTarget ChatTarget);

public sealed record ReportMessageResult(bool Success, string? Error, int NextCommentCursor, string? SelectedOptionText)
{
    public static ReportMessageResult Ok(int nextCommentCursor, string? selectedOptionText)
        => new(true, null, nextCommentCursor, selectedOptionText);

    public static ReportMessageResult Fail(string error, int nextCommentCursor)
        => new(false, error, nextCommentCursor, null);
}
