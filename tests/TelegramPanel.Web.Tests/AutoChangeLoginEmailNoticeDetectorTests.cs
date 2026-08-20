using TelegramPanel.Core.Models;
using TelegramPanel.Web.Services;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class AutoChangeLoginEmailNoticeDetectorTests
{
    [Fact]
    public void BuildWindowUtc_centers_configured_lookback_window()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        var (from, to) = AutoChangeLoginEmailNoticeDetector.BuildWindowUtc(now, triggerDaysAgo: 6, triggerWindowHours: 24);

        Assert.Equal(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero), to);
    }

    [Fact]
    public void FindBestMatch_accepts_login_email_settings_notice_inside_window()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var messages = new[]
        {
            new TelegramSystemMessage(1, new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc), "Settings > Privacy & Security > Login Email."),
            new TelegramSystemMessage(2, new DateTime(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc), "You can cancel this request in Settings > Privacy & Security > Login Email."),
        };

        var match = AutoChangeLoginEmailNoticeDetector.FindBestMatch(messages, now, 6, 24);

        Assert.NotNull(match);
        Assert.Equal(2, match!.MessageId);
        Assert.Equal(new DateTime(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc), match.DateUtc);
    }

    [Fact]
    public void FindBestMatch_rejects_login_email_text_outside_window()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var messages = new[]
        {
            new TelegramSystemMessage(1, new DateTime(2026, 8, 5, 8, 0, 0, DateTimeKind.Utc), "Settings > Privacy & Security > Login Email."),
        };

        var match = AutoChangeLoginEmailNoticeDetector.FindBestMatch(messages, now, 6, 24);

        Assert.Null(match);
    }

    [Fact]
    public void Decide_skips_without_cloud_mail_even_when_notice_matches()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var messages = new[]
        {
            new TelegramSystemMessage(1, new DateTime(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc), "Settings > Privacy & Security > Login Email."),
        };

        var decision = AutoChangeLoginEmailNoticeDetector.Decide(
            cloudMailConfigured: false,
            targetEmail: "8613800000000@example.com",
            force: false,
            messages: messages,
            nowUtc: now,
            triggerDaysAgo: 6,
            triggerWindowHours: 24);

        Assert.False(decision.ShouldAttempt);
        Assert.Equal(AutoChangeLoginEmailTaskResult.Skipped, decision.Result);
        Assert.Contains("Cloud Mail", decision.Message);
    }

    [Fact]
    public void Decide_attempts_only_when_notice_matches_by_default()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var messages = new[]
        {
            new TelegramSystemMessage(7, new DateTime(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc), "You can cancel this request in Settings > Privacy & Security > Login Email."),
        };

        var decision = AutoChangeLoginEmailNoticeDetector.Decide(
            cloudMailConfigured: true,
            targetEmail: "8613800000000@example.com",
            force: false,
            messages: messages,
            nowUtc: now,
            triggerDaysAgo: 6,
            triggerWindowHours: 24);

        Assert.True(decision.ShouldAttempt);
        Assert.Equal(7, decision.Match?.MessageId);
    }

    [Fact]
    public void Decide_skips_when_target_email_cannot_be_generated()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        var decision = AutoChangeLoginEmailNoticeDetector.Decide(
            cloudMailConfigured: true,
            targetEmail: "",
            force: false,
            messages: Array.Empty<TelegramSystemMessage>(),
            nowUtc: now,
            triggerDaysAgo: 6,
            triggerWindowHours: 24);

        Assert.False(decision.ShouldAttempt);
        Assert.Equal(AutoChangeLoginEmailTaskResult.Skipped, decision.Result);
        Assert.Contains("目标邮箱", decision.Message);
    }

    [Fact]
    public void Decide_force_attempts_without_matching_notice()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        var decision = AutoChangeLoginEmailNoticeDetector.Decide(
            cloudMailConfigured: true,
            targetEmail: "8613800000000@example.com",
            force: true,
            messages: Array.Empty<TelegramSystemMessage>(),
            nowUtc: now,
            triggerDaysAgo: 6,
            triggerWindowHours: 24);

        Assert.True(decision.ShouldAttempt);
        Assert.Null(decision.Match);
        Assert.Contains("强制模式", decision.Message);
    }

    [Fact]
    public void NormalizeDomains_accepts_multiple_separators_and_legacy_email_input()
    {
        var domains = AutoChangeLoginEmailTaskHandler.NormalizeDomains(new[]
        {
            "@old.example, new.example",
            "user@third.example;OLD.example",
            "mailto:fourth.example"
        });

        Assert.Equal(new[] { "old.example", "new.example", "third.example", "fourth.example" }, domains);
    }

    [Fact]
    public void PickTargetDomain_excludes_previous_login_email_domain_when_possible()
    {
        var previousDomain = AutoChangeLoginEmailTaskHandler.ExtractDomainFromLoginEmailPattern("a***@old.example");

        var picked = AutoChangeLoginEmailTaskHandler.PickTargetDomain(
            new[] { "old.example", "new.example" },
            previousDomain);

        Assert.Equal("old.example", previousDomain);
        Assert.Equal("new.example", picked);
    }

    [Fact]
    public void PickTargetDomain_keeps_single_domain_for_backward_compatibility()
    {
        var picked = AutoChangeLoginEmailTaskHandler.PickTargetDomain(
            new[] { "old.example" },
            "old.example");

        Assert.Equal("old.example", picked);
    }

    [Fact]
    public void BuildEmailByPhone_normalizes_domain_input()
    {
        var email = AutoChangeLoginEmailTaskHandler.BuildEmailByPhone("+86 138 0000 0000", "User@Example.COM.");

        Assert.Equal("8613800000000@example.com", email);
    }
}
