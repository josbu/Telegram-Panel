using System.Text.RegularExpressions;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class TelegramRecoveryWiringTests
{
    private static readonly string ServiceSource = File.ReadAllText(Path.Combine(
        TestRepositoryRoot.Find(),
        "src",
        "TelegramPanel.Core",
        "Services",
        "Telegram",
        "AccountTelegramToolsService.cs"));

    [Fact]
    public void 状态刷新目标解析和任务入口均接入瞬时连接重试()
    {
        Assert.Matches(
            "RefreshAccountStatusAsync\\([\\s\\S]*?TelegramTransientConnectionRetry\\.ExecuteAsync[\\s\\S]*?private async Task TryPersistStatusAsync",
            ServiceSource);
        Assert.Matches(
            "ResolveChatTargetAsync\\([\\s\\S]*?TelegramTransientConnectionRetry\\.ExecuteAsync[\\s\\S]*?SendMessageToResolvedChatAsync",
            ServiceSource);
        Assert.Matches(
            "JoinChatOrChannelAsync\\([\\s\\S]*?TelegramTransientConnectionRetry\\.ExecuteAsync[\\s\\S]*?LeaveChatOrChannelAsync",
            ServiceSource);
        Assert.Matches(
            "StartExternalBotAsync\\([\\s\\S]*?TelegramTransientConnectionRetry\\.ExecuteAsync[\\s\\S]*?StopExternalBotAsync",
            ServiceSource);
        Assert.Matches(
            "StopExternalBotAsync\\([\\s\\S]*?TelegramTransientConnectionRetry\\.ExecuteAsync[\\s\\S]*?public sealed record ResolvedChatTarget",
            ServiceSource);
    }

    [Fact]
    public void 账号活跃目标解析先识别Bot私聊再回退群组频道()
    {
        Assert.Matches(
            "TryNormalizeTelegramBotUsername\\(raw[\\s\\S]*?TryResolveBotChatTargetAsync[\\s\\S]*?client\\.AnalyzeInviteLink",
            ServiceSource);
    }

    [Fact]
    public void 目标解析的Telegram读取均有超时与取消边界()
    {
        Assert.Matches(
            "var chat = await ExecuteTelegramRequestAsync\\([\\s\\S]*?client\\.AnalyzeInviteLink",
            ServiceSource);
        Assert.Matches(
            "var dialogs = await ExecuteTelegramRequestAsync\\([\\s\\S]*?client\\.Messages_GetAllDialogs",
            ServiceSource);
    }

    [Fact]
    public void 目标解析在调用方取消时传播取消而不是普通失败()
    {
        Assert.Contains(
            "catch (Exception) when (cancellationToken.IsCancellationRequested)",
            ServiceSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "throw new OperationCanceledException(cancellationToken);",
            ServiceSource,
            StringComparison.Ordinal);
    }
}
