using System.Text.Json;
using TelegramPanel.Web.Services;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class ChannelGroupPrivateCreateTaskHandlerTests
{
    [Fact]
    public void RecordFailure_keeps_latest_twenty_entries_with_readable_reason()
    {
        var config = new ChannelGroupPrivateCreateTaskConfig();

        for (var accountId = 1; accountId <= 22; accountId++)
        {
            ChannelGroupPrivateCreateTaskHandler.RecordFailure(
                config,
                accountId,
                ChannelGroupAutomationTaskObjectTypes.Channel,
                $"频道 {accountId}",
                accountId == 22 ? "第一行\r\n第二行" : $"错误 {accountId}",
                new DateTime(2026, 8, 2, 0, 0, accountId, DateTimeKind.Utc));
        }

        Assert.Equal(ChannelGroupPrivateCreateTaskHandler.MaxRecentFailures, config.RecentFailures.Count);
        Assert.Equal(3, config.RecentFailures[0].AccountId);
        Assert.Equal(22, config.RecentFailures[^1].AccountId);
        Assert.Equal("第一行 第二行", config.RecentFailures[^1].Reason);
    }

    [Fact]
    public void SerializeConfig_persists_failure_contract_and_limits_reason_length()
    {
        var config = new ChannelGroupPrivateCreateTaskConfig();
        ChannelGroupPrivateCreateTaskHandler.RecordFailure(
            config,
            accountId: 15,
            targetType: ChannelGroupAutomationTaskObjectTypes.Group,
            target: null,
            reason: new string('错', ChannelGroupPrivateCreateTaskHandler.MaxFailureReasonLength + 10),
            timeUtc: new DateTime(2026, 8, 2, 1, 2, 3, DateTimeKind.Utc));

        var json = ChannelGroupPrivateCreateTaskHandler.SerializeConfig(config);
        using var document = JsonDocument.Parse(json);
        var failure = document.RootElement.GetProperty("recent_failures")[0];

        Assert.Equal(15, failure.GetProperty("account_id").GetInt32());
        Assert.Equal("group", failure.GetProperty("target_type").GetString());
        Assert.Equal("-", failure.GetProperty("target").GetString());
        Assert.Equal(
            ChannelGroupPrivateCreateTaskHandler.MaxFailureReasonLength,
            failure.GetProperty("reason").GetString()!.Length);
    }

    [Fact]
    public void NormalizeFailureReason_redacts_proxy_credentials_and_secrets()
    {
        var normalized = ChannelGroupPrivateCreateTaskHandler.NormalizeFailureReason(
            "连接 socks5://demo:pass@example.test:1080 失败，token=abc123");

        Assert.Equal("连接 socks5://***@example.test:1080 失败，token=***", normalized);
        Assert.DoesNotContain("demo:pass", normalized);
        Assert.DoesNotContain("abc123", normalized);
    }
}
