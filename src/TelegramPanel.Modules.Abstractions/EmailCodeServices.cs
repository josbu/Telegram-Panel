namespace TelegramPanel.Modules;

/// <summary>
/// Telegram 邮箱验证码获取服务（供模块调用）。\n/// 典型场景：部分客户端会把登录/确认验证码发送到邮箱，模块可通过该服务统一取码。
/// </summary>
public interface ITelegramEmailCodeService
{
    /// <summary>
    /// 根据手机号数字串（E.164 digits，无 +）拼出邮箱地址（phoneDigits@domain）。
    /// </summary>
    string BuildEmailByPhoneDigits(string phoneDigits);

    /// <summary>
    /// 获取指定邮箱的最新 Telegram 验证码（优先解析 Telegram 常见模板）。
    /// </summary>
    Task<TelegramEmailCodeResult> TryGetLatestCodeByEmailAsync(
        string toEmail,
        DateTimeOffset? sinceUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定手机号对应邮箱的最新 Telegram 验证码（phoneDigits@domain）。
    /// </summary>
    Task<TelegramEmailCodeResult> TryGetLatestCodeByPhoneDigitsAsync(
        string phoneDigits,
        DateTimeOffset? sinceUtc = null,
        CancellationToken cancellationToken = default);
}

public sealed record TelegramEmailCodeResult(
    bool Success,
    string? Code,
    string? Error,
    string? ToEmail,
    string? Subject,
    DateTimeOffset? CreatedUtc);

