using TelegramPanel.Core.Models;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class TelegramAccountWasteJudgeTests
{
    [Theory]
    [InlineData("连接失败")]
    [InlineData("连接失败：A task was canceled.")]
    [InlineData("请求超时")]
    [InlineData("刷新失败")]
    [InlineData("创建频道探测失败")]
    [InlineData("无法获取账号资料")]
    [InlineData("已取消")]
    [InlineData("触发限流（FLOOD_WAIT）")]
    public void 瞬时或不确定状态不会判为废号(string summary)
    {
        var status = CreateStatus(summary);

        var isWaste = TelegramAccountWasteJudge.TryGetWasteReason(status, out var reason);

        Assert.False(isWaste);
        Assert.Empty(reason);
        Assert.False(TelegramAccountWasteJudge.IsWaste(status));
    }

    [Theory]
    [InlineData("账号被封禁", "账号被封禁/停用")]
    [InlineData("被封禁/停用（PHONE_NUMBER_BANNED）", "账号被封禁/停用")]
    [InlineData("账号被停用（USER_DEACTIVATED）", "账号被封禁/停用")]
    [InlineData("Session 失效（AUTH_KEY_UNREGISTERED）", "Session 失效")]
    [InlineData("Session 冲突（AUTH_KEY_DUPLICATED）", "Session 冲突")]
    [InlineData("Session 已被撤销（SESSION_REVOKED）", "Session 已被撤销")]
    [InlineData("Session 无法读取（Can't read session block）", "Session 损坏/不匹配")]
    [InlineData("账号受限（Restricted）", "账号受限（Restricted）")]
    [InlineData("账号被冻结（FROZEN_METHOD_INVALID）", "账号被冻结")]
    [InlineData("需要两步验证密码（SESSION_PASSWORD_NEEDED）", "需要两步验证密码（未登录）")]
    [InlineData("账号已注销/被删除", "账号已注销/被删除")]
    public void 明确不可恢复状态仍判为废号(string summary, string expectedReason)
    {
        var status = CreateStatus(summary);

        var isWaste = TelegramAccountWasteJudge.TryGetWasteReason(status, out var reason);

        Assert.True(isWaste);
        Assert.Equal(expectedReason, reason);
        Assert.True(TelegramAccountWasteJudge.IsWaste(status));
    }

    [Fact]
    public void 已删除资料快照优先判为废号()
    {
        var status = CreateStatus("正常", CreateProfile(isDeleted: true));

        Assert.True(TelegramAccountWasteJudge.TryGetWasteReason(status, out var reason));
        Assert.Equal("账号已注销/被删除", reason);
    }

    [Fact]
    public void 受限资料快照优先判为废号()
    {
        var status = CreateStatus("正常", CreateProfile(isRestricted: true));

        Assert.True(TelegramAccountWasteJudge.TryGetWasteReason(status, out var reason));
        Assert.Equal("账号受限（Restricted）", reason);
    }

    [Fact]
    public void 空状态不会判为废号()
    {
        Assert.False(TelegramAccountWasteJudge.TryGetWasteReason(null, out var reason));
        Assert.Empty(reason);
    }

    private static TelegramAccountStatusResult CreateStatus(
        string summary,
        TelegramAccountProfile? profile = null) =>
        new(
            Ok: false,
            Summary: summary,
            Details: null,
            CheckedAtUtc: DateTime.UtcNow,
            Profile: profile);

    private static TelegramAccountProfile CreateProfile(
        bool isDeleted = false,
        bool isRestricted = false) =>
        new(
            UserId: 1,
            Phone: null,
            Username: null,
            FirstName: null,
            LastName: null,
            IsDeleted: isDeleted,
            IsScam: false,
            IsFake: false,
            IsRestricted: isRestricted,
            IsVerified: false,
            IsPremium: false);
}
