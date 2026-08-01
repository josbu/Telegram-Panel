using TelegramPanel.Web.Services;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class UserChatActiveSendRetryPolicyTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(8, 5)]
    public void 重试次数限制在零到五次(int configured, int expected)
    {
        Assert.Equal(expected, UserChatActiveSendRetryPolicy.NormalizeMaxRetries(configured));
    }

    [Theory]
    [InlineData("连接失败：A task was canceled.")]
    [InlineData("请求超时：The operation timed out")]
    [InlineData("连接失败：CHANNEL_INVALID")]
    [InlineData("PEER_ID_INVALID")]
    [InlineData("CHAT_ID_INVALID")]
    public void 瞬时连接和失效Peer错误允许重试(string error)
    {
        Assert.True(UserChatActiveSendRetryPolicy.ShouldRetry(error));
    }

    [Theory]
    [InlineData("")]
    [InlineData("账号权限不足：CHAT_WRITE_FORBIDDEN")]
    [InlineData("Session 失效（AUTH_KEY_UNREGISTERED）")]
    [InlineData("词典模板解析结果为空，无法发送")]
    [InlineData("触发限流（FLOOD_WAIT）")]
    public void 永久错误不会自动重试(string error)
    {
        Assert.False(UserChatActiveSendRetryPolicy.ShouldRetry(error));
    }

    [Fact]
    public void 最终失败包含已执行的重试次数()
    {
        Assert.Equal(
            "连接失败：CHANNEL_INVALID（自动重试 3 次后仍失败）",
            UserChatActiveSendRetryPolicy.DescribeFinalFailure("连接失败：CHANNEL_INVALID", 3));
    }
}
