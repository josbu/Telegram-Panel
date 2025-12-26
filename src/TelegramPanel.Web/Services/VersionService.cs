using System.Reflection;

namespace TelegramPanel.Web.Services;

/// <summary>
/// 版本信息服务（静态类），用于获取应用程序版本号
/// </summary>
public static class VersionService
{
    private static readonly Lazy<string> _version = new(GetVersionFromAssembly);
    private static readonly Lazy<string> _fullVersion = new(GetFullVersionFromAssembly);

    /// <summary>
    /// 获取简化版本号（如：1.0.0）
    /// </summary>
    public static string Version => _version.Value;

    /// <summary>
    /// 获取完整版本信息（包含 InformationalVersion，如：1.0.0+commitHash）
    /// </summary>
    public static string FullVersion => _fullVersion.Value;

    private static string GetVersionFromAssembly()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        // 优先使用 InformationalVersion（更友好的版本号）
        var infoVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(infoVersion))
        {
            // 移除可能的 +commitHash 后缀，只保留版本号部分
            var plusIndex = infoVersion.IndexOf('+');
            return plusIndex > 0 ? infoVersion[..plusIndex] : infoVersion;
        }

        // 回退到 AssemblyVersion
        var version = assembly.GetName().Version;
        return version != null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "Unknown";
    }

    private static string GetFullVersionFromAssembly()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        var infoVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return !string.IsNullOrWhiteSpace(infoVersion)
            ? infoVersion
            : GetVersionFromAssembly();
    }
}
