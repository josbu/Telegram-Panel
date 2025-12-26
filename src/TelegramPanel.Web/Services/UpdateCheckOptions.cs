namespace TelegramPanel.Web.Services;

public sealed class UpdateCheckOptions
{
    /// <summary>
    /// 是否启用“检查新版本”功能
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// GitHub 仓库（owner/repo），用于检查 Releases/Tags
    /// </summary>
    public string Repository { get; set; } = "moeacgx/Telegram-Panel";

    /// <summary>
    /// 缓存分钟数：避免频繁请求 GitHub API（默认 30 分钟）
    /// </summary>
    public int CacheMinutes { get; set; } = 30;
}

