namespace TelegramPanel.Web.Tests;

internal static class TestRepositoryRoot
{
    internal static string Find()
    {
        var startDirectories = new[]
        {
            Environment.GetEnvironmentVariable("TELEGRAM_PANEL_REPOSITORY_ROOT"),
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        }.Where(path => !string.IsNullOrWhiteSpace(path));

        foreach (var startDirectory in startDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            for (var directory = new DirectoryInfo(startDirectory!);
                 directory != null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))
                    && Directory.Exists(Path.Combine(directory.FullName, ".github", "workflows")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("未找到仓库根目录");
    }
}
