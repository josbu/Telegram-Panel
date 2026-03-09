using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Services.Telegram;
using TL;
using WTelegram;

namespace TelegramPanel.Modules.MessageReportTask.Services;

public sealed class TelegramMessageReportService
{
    private readonly AccountTelegramToolsService _accountTools;
    private readonly ITelegramClientPool _clientPool;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelegramMessageReportService> _logger;

    public TelegramMessageReportService(
        AccountTelegramToolsService accountTools,
        ITelegramClientPool clientPool,
        IConfiguration configuration,
        ILogger<TelegramMessageReportService> logger)
    {
        _accountTools = accountTools;
        _clientPool = clientPool;
        _configuration = configuration;
        _logger = logger;
    }

    public sealed record ResolvedReportMessageTarget(
        AccountTelegramToolsService.ResolvedChatTarget Chat,
        int MessageId,
        string CanonicalLink,
        string RawInput);

    public sealed record ReportMessageResult(
        bool Success,
        string? Error,
        string? SelectedOptionText,
        bool ShouldRemoveTarget);

    private sealed record ParsedMessageLink(string ResolveTarget, int MessageId, string CanonicalLink);

    public async Task<(bool Success, string? Error, ResolvedReportMessageTarget? Target)> ResolveMessageTargetAsync(
        int accountId,
        string rawInput,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parsed = ParseMessageLink(rawInput);
            var resolved = await _accountTools.ResolveChatTargetAsync(accountId, parsed.ResolveTarget, cancellationToken);
            if (!resolved.Success || resolved.Target == null)
                return (false, resolved.Error ?? "无法解析消息链接", null);

            return (true, null, new ResolvedReportMessageTarget(resolved.Target, parsed.MessageId, parsed.CanonicalLink, (rawInput ?? string.Empty).Trim()));
        }
        catch (Exception ex)
        {
            var message = ex is ArgumentException or FormatException or InvalidOperationException
                ? ex.Message
                : BuildTelegramError(ex);
            return (false, message, null);
        }
    }

    public async Task<ReportMessageResult> ReportMessageAsync(
        int accountId,
        ResolvedReportMessageTarget target,
        MessageReportTaskConfig config,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await EnsureConnectedClientAsync(accountId, target.Chat.CanonicalId, cancellationToken);

            byte[] option = Array.Empty<byte>();
            var comment = string.Empty;
            string? selectedOptionText = null;

            var result = await ExecuteTelegramRequestAsync(
                accountId,
                $"举报消息({target.CanonicalLink})",
                () => client.Messages_Report(target.Chat.Peer, new[] { target.MessageId }, option, comment),
                cancellationToken,
                resetClientOnTimeout: true);

            for (var depth = 0; depth < 8; depth++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (result)
                {
                    case ReportResultReported:
                        return new ReportMessageResult(true, null, selectedOptionText, true);

                    case ReportResultChooseOption choose:
                    {
                        var selected = SelectOption(choose.options, config);
                        if (selected == null)
                        {
                            var available = BuildOptionListText(choose.options);
                            var error = available.Length == 0
                                ? "Telegram 返回了空的举报选项列表"
                                : $"未匹配到举报类型，对应可选项：{available}";
                            return new ReportMessageResult(false, error, selectedOptionText, true);
                        }

                        option = selected.option ?? Array.Empty<byte>();
                        selectedOptionText = NormalizeNullableText(selected.text);

                        result = await ExecuteTelegramRequestAsync(
                            accountId,
                            $"选择举报类型({selectedOptionText ?? "未命名选项"})",
                            () => client.Messages_Report(target.Chat.Peer, new[] { target.MessageId }, option, comment),
                            cancellationToken,
                            resetClientOnTimeout: true);
                        break;
                    }

                    case ReportResultAddComment addComment:
                    {
                        option = addComment.option ?? option;
                        comment = BuildComment(config.Comment, target);
                        var optional = addComment.flags.HasFlag(ReportResultAddComment.Flags.optional);
                        if (string.IsNullOrWhiteSpace(comment) && !optional)
                            return new ReportMessageResult(false, "当前举报类型要求填写举报文案，请先补充文案后再执行", selectedOptionText, true);

                        result = await ExecuteTelegramRequestAsync(
                            accountId,
                            $"提交举报文案({target.CanonicalLink})",
                            () => client.Messages_Report(target.Chat.Peer, new[] { target.MessageId }, option, comment),
                            cancellationToken,
                            resetClientOnTimeout: true);
                        break;
                    }

                    default:
                        return new ReportMessageResult(false, $"不支持的举报返回类型：{result.GetType().Name}", selectedOptionText, true);
                }
            }

            return new ReportMessageResult(false, "举报流程超过安全重试次数，请稍后再试", selectedOptionText, false);
        }
        catch (Exception ex)
        {
            var error = BuildTelegramError(ex);
            return new ReportMessageResult(false, error, null, ShouldRemoveTargetAfterFailure(error));
        }
    }

    private async Task<Client> EnsureConnectedClientAsync(int accountId, string resolveHint, CancellationToken cancellationToken)
    {
        var existing = _clientPool.GetClient(accountId);
        if (existing?.User != null)
            return existing;

        var resolved = await _accountTools.ResolveChatTargetAsync(accountId, resolveHint, cancellationToken);
        if (!resolved.Success)
            throw new InvalidOperationException(resolved.Error ?? "无法建立 Telegram 客户端连接");

        var client = _clientPool.GetClient(accountId);
        if (client?.User != null)
            return client;

        throw new InvalidOperationException("无法建立 Telegram 客户端连接");
    }

    private static ParsedMessageLink ParseMessageLink(string rawInput)
    {
        var raw = (rawInput ?? string.Empty).Trim();
        if (raw.Length == 0)
            throw new ArgumentException("消息链接为空", nameof(rawInput));

        if (raw.StartsWith("tg://", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(raw, UriKind.Absolute, out var tgUri)
            && string.Equals(tgUri.Host, "privatepost", StringComparison.OrdinalIgnoreCase))
        {
            var query = ParseQueryString(tgUri.Query);
            if (!query.TryGetValue("channel", out var channelText) || string.IsNullOrWhiteSpace(channelText))
                throw new ArgumentException("tg://privatepost 缺少 channel 参数", nameof(rawInput));
            if (!query.TryGetValue("post", out var postText) || string.IsNullOrWhiteSpace(postText))
                throw new ArgumentException("tg://privatepost 缺少 post 参数", nameof(rawInput));

            var internalId = NormalizeInternalChatId(channelText);
            var messageId = ParsePositiveMessageId(postText);
            return new ParsedMessageLink($"-100{internalId}", messageId, $"https://t.me/c/{internalId}/{messageId}");
        }

        var normalizedUrl = raw;
        if (normalizedUrl.StartsWith("t.me/", StringComparison.OrdinalIgnoreCase)
            || normalizedUrl.StartsWith("telegram.me/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedUrl = "https://" + normalizedUrl;
        }

        if (Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Host, "t.me", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "telegram.me", StringComparison.OrdinalIgnoreCase)))
        {
            var segments = (uri.AbsolutePath ?? string.Empty)
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return ParseSegments(segments, rawInput);
        }

        var plain = raw;
        var queryIndex = plain.IndexOfAny(new[] { '?', '#' });
        if (queryIndex >= 0)
            plain = plain[..queryIndex];

        var pathSegments = plain
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return ParseSegments(pathSegments, rawInput);
    }

    private static ParsedMessageLink ParseSegments(string[] segments, string? rawInput)
    {
        if (segments.Length >= 3 && string.Equals(segments[0], "c", StringComparison.OrdinalIgnoreCase))
        {
            var internalId = NormalizeInternalChatId(segments[1]);
            var messageId = ParsePositiveMessageId(segments[2]);
            return new ParsedMessageLink($"-100{internalId}", messageId, $"https://t.me/c/{internalId}/{messageId}");
        }

        if (segments.Length >= 3 && (string.Equals(segments[0], "b", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(segments[0], "s", StringComparison.OrdinalIgnoreCase)))
        {
            var target = NormalizeTargetToken(segments[1]);
            var messageId = ParsePositiveMessageId(segments[2]);
            return new ParsedMessageLink(target, messageId, $"https://t.me/{target.TrimStart('@')}/{messageId}");
        }

        if (segments.Length >= 2)
        {
            var target = NormalizeTargetToken(segments[0]);
            var messageId = ParsePositiveMessageId(segments[1]);
            if (long.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return new ParsedMessageLink(target, messageId, $"{target}/{messageId}");

            return new ParsedMessageLink(target, messageId, $"https://t.me/{target.TrimStart('@')}/{messageId}");
        }

        throw new ArgumentException($"不支持的消息链接格式：{(rawInput ?? string.Empty).Trim()}", nameof(rawInput));
    }

    private static string NormalizeInternalChatId(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.StartsWith("-100", StringComparison.Ordinal))
            text = text[4..];

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            throw new FormatException($"私有消息链接中的频道 ID 无效：{value}");

        return parsed.ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizeTargetToken(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
            throw new FormatException("消息链接中缺少目标标识");

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return text;

        return text.TrimStart('@');
    }

    private static int ParsePositiveMessageId(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            throw new FormatException($"消息 ID 无效：{value}");
        return parsed;
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = (query ?? string.Empty).Trim();
        if (text.StartsWith("?", StringComparison.Ordinal))
            text = text[1..];
        if (text.Length == 0)
            return result;

        foreach (var pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString((parts[0] ?? string.Empty).Trim());
            if (key.Length == 0)
                continue;

            var value = parts.Length > 1 ? Uri.UnescapeDataString((parts[1] ?? string.Empty).Trim()) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static MessageReportOption? SelectOption(IEnumerable<MessageReportOption>? options, MessageReportTaskConfig config)
    {
        var list = (options ?? Array.Empty<MessageReportOption>())
            .Where(x => x != null)
            .ToList();
        if (list.Count == 0)
            return null;

        if (string.Equals(config.ReportPreset, MessageReportTaskPresets.FirstAvailable, StringComparison.OrdinalIgnoreCase))
            return list[0];

        var normalizedKeywords = BuildNormalizedKeywords(config);
        if (normalizedKeywords.Count == 0)
            return null;

        foreach (var option in list)
        {
            var text = NormalizeKeyword(option.text);
            if (text.Length == 0)
                continue;

            if (normalizedKeywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal)))
                return option;
        }

        return null;
    }

    private static List<string> BuildNormalizedKeywords(MessageReportTaskConfig config)
    {
        var custom = (config.OptionKeywords ?? new List<string>())
            .Select(NormalizeKeyword)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (custom.Count > 0)
            return custom;

        return GetPresetKeywords(config.ReportPreset)
            .Select(NormalizeKeyword)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> GetPresetKeywords(string? preset)
    {
        return (preset ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            MessageReportTaskPresets.Spam => new[] { "spam", "垃圾", "骚扰", "广告" },
            MessageReportTaskPresets.Violence => new[] { "violence", "violent", "暴力", "威胁" },
            MessageReportTaskPresets.Pornography => new[] { "porn", "pornography", "色情", "淫秽" },
            MessageReportTaskPresets.ChildAbuse => new[] { "child", "childabuse", "儿童", "未成年人" },
            MessageReportTaskPresets.Copyright => new[] { "copyright", "侵权", "版权" },
            MessageReportTaskPresets.IllegalDrugs => new[] { "drugs", "illegaldrugs", "毒品", "违禁药物" },
            MessageReportTaskPresets.PersonalDetails => new[] { "personal", "personaldetails", "隐私", "个人信息" },
            MessageReportTaskPresets.Other => new[] { "other", "其他" },
            _ => Array.Empty<string>()
        };
    }

    private static string BuildComment(string? template, ResolvedReportMessageTarget target)
    {
        var text = (template ?? string.Empty).Trim();
        if (text.Length == 0)
            return string.Empty;

        return text
            .Replace("{消息链接}", target.CanonicalLink, StringComparison.OrdinalIgnoreCase)
            .Replace("{message_link}", target.CanonicalLink, StringComparison.OrdinalIgnoreCase)
            .Replace("{聊天标题}", target.Chat.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{chat_title}", target.Chat.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{消息ID}", target.MessageId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{message_id}", target.MessageId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildOptionListText(IEnumerable<MessageReportOption>? options)
    {
        return string.Join(" / ", (options ?? Array.Empty<MessageReportOption>())
            .Select(x => NormalizeNullableText(x?.text))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizeKeyword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch >= 0x4e00)
                builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string? NormalizeNullableText(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length == 0 ? null : text;
    }

    private string BuildTelegramError(Exception ex)
    {
        var (summary, details) = AccountTelegramToolsService.MapTelegramException(ex);
        return string.IsNullOrWhiteSpace(details) ? summary : $"{summary}：{details}";
    }

    private static bool ShouldRemoveTargetAfterFailure(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return false;

        if (LooksLikeTemporaryFailure(error))
            return false;

        return error.Contains("ALREADY_REPORTED", StringComparison.OrdinalIgnoreCase)
               || error.Contains("MESSAGE_ALREADY_REPORTED", StringComparison.OrdinalIgnoreCase)
               || error.Contains("MESSAGE_ID_INVALID", StringComparison.OrdinalIgnoreCase)
               || error.Contains("MSG_ID_INVALID", StringComparison.OrdinalIgnoreCase)
               || error.Contains("MESSAGE_NOT_FOUND", StringComparison.OrdinalIgnoreCase)
               || error.Contains("CHAT_ID_INVALID", StringComparison.OrdinalIgnoreCase)
               || error.Contains("PEER_ID_INVALID", StringComparison.OrdinalIgnoreCase)
               || error.Contains("CHANNEL_INVALID", StringComparison.OrdinalIgnoreCase)
               || error.Contains("CHANNEL_PRIVATE", StringComparison.OrdinalIgnoreCase)
               || error.Contains("GROUPED_MEDIA_INVALID", StringComparison.OrdinalIgnoreCase)
               || error.Contains("未匹配到举报类型", StringComparison.OrdinalIgnoreCase)
               || error.Contains("要求填写举报文案", StringComparison.OrdinalIgnoreCase)
               || error.Contains("不支持的举报返回类型", StringComparison.OrdinalIgnoreCase)
               || error.Contains("链接格式", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeTemporaryFailure(string error)
    {
        return error.Contains("FLOOD_WAIT", StringComparison.OrdinalIgnoreCase)
               || error.Contains("请求超时", StringComparison.OrdinalIgnoreCase)
               || error.Contains("timeout", StringComparison.OrdinalIgnoreCase)
               || error.Contains("timed out", StringComparison.OrdinalIgnoreCase)
               || error.Contains("网络", StringComparison.OrdinalIgnoreCase)
               || error.Contains("NETWORK", StringComparison.OrdinalIgnoreCase);
    }

    private TimeSpan GetTelegramRequestTimeout()
    {
        var seconds = int.TryParse(_configuration["Telegram:RequestTimeoutSeconds"], out var parsedSeconds)
            ? parsedSeconds
            : 90;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 15, 600));
    }

    private async Task<T> ExecuteTelegramRequestAsync<T>(
        int accountId,
        string operation,
        Func<Task<T>> action,
        CancellationToken cancellationToken,
        bool resetClientOnTimeout)
    {
        var timeout = GetTelegramRequestTimeout();

        try
        {
            return await action().WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Telegram request timed out after {TimeoutSeconds}s for account {AccountId}: {Operation}",
                timeout.TotalSeconds,
                accountId,
                operation);

            if (resetClientOnTimeout)
                await _clientPool.RemoveClientAsync(accountId);

            throw new TimeoutException($"Telegram 请求超时：{operation} 超过 {timeout.TotalSeconds:0} 秒，可能是账号受限、网络异常或代理异常");
        }
    }
}
