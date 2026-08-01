namespace TelegramPanel.Web.Services;

/// <summary>
/// 群聊活跃任务发送失败后的有限重试策略。
/// 只重试连接、超时和失效 peer 等可恢复错误，避免对权限或 Session 永久错误重复发送。
/// </summary>
internal static class UserChatActiveSendRetryPolicy
{
    internal const int MaximumRetries = 5;

    internal static int NormalizeMaxRetries(int value) =>
        Math.Clamp(value, 0, MaximumRetries);

    internal static bool ShouldRetry(string? error)
    {
        var text = (error ?? string.Empty).Trim();
        if (text.Length == 0)
            return false;

        return text.Contains("连接失败", StringComparison.OrdinalIgnoreCase)
               || text.Contains("请求超时", StringComparison.OrdinalIgnoreCase)
               || text.Contains("A task was canceled", StringComparison.OrdinalIgnoreCase)
               || text.Contains("TaskCanceledException", StringComparison.OrdinalIgnoreCase)
               || text.Contains("CHANNEL_INVALID", StringComparison.OrdinalIgnoreCase)
               || text.Contains("PEER_ID_INVALID", StringComparison.OrdinalIgnoreCase)
               || text.Contains("CHAT_ID_INVALID", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldResetClient(string? error)
    {
        var text = (error ?? string.Empty).Trim();
        return text.Contains("连接失败", StringComparison.OrdinalIgnoreCase)
               || text.Contains("请求超时", StringComparison.OrdinalIgnoreCase)
               || text.Contains("A task was canceled", StringComparison.OrdinalIgnoreCase)
               || text.Contains("TaskCanceledException", StringComparison.OrdinalIgnoreCase);
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
}
