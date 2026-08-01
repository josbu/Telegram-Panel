namespace TelegramPanel.Web.Services;

/// <summary>
/// 群聊活跃任务发送失败后的有限重试策略。
/// 只重试连接、超时和失效 peer 等可恢复错误，避免对权限或 Session 永久错误重复发送。
/// </summary>
internal static class UserChatActiveSendRetryPolicy
{
    internal const int MaximumRetries = 5;

    private static readonly string[] ConnectionRelatedErrors =
    [
        "连接失败",
        "请求超时",
        "A task was canceled",
        "TaskCanceledException"
    ];

    private static readonly string[] InvalidPeerErrors =
    [
        "CHANNEL_INVALID",
        "PEER_ID_INVALID",
        "CHAT_ID_INVALID"
    ];

    internal static int NormalizeMaxRetries(int value) =>
        Math.Clamp(value, 0, MaximumRetries);

    internal static bool ShouldRetry(string? error)
    {
        var text = (error ?? string.Empty).Trim();
        if (text.Length == 0)
            return false;

        return ContainsAny(text, ConnectionRelatedErrors)
               || ContainsAny(text, InvalidPeerErrors);
    }

    internal static bool ShouldResetClient(string? error)
    {
        var text = (error ?? string.Empty).Trim();
        return ContainsAny(text, ConnectionRelatedErrors);
    }

    internal static int GetDelayMilliseconds(int retryAttempt) =>
        Math.Clamp(retryAttempt, 1, MaximumRetries) * 1000;

    internal static string DescribeFinalFailure(string? error, int retryAttempts)
    {
        var reason = string.IsNullOrWhiteSpace(error) ? "失败" : error.Trim();
        return retryAttempts <= 0
            ? reason
            : $"{reason}（自动重试 {retryAttempts} 次后仍失败）";
    }

    private static bool ContainsAny(string text, IEnumerable<string> markers) =>
        markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
