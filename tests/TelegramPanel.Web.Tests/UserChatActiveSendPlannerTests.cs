using TelegramPanel.Web.Services;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class UserChatActiveSendPlannerTests
{
    [Fact]
    public void 有限任务消息数超过可用账号时按可用账号封顶()
    {
        var plan = UserChatActiveSendPlanner.BuildFiniteRunPlan(
            eligibleAccountCount: 1,
            requestedMessageCount: 10,
            dictionaryCount: 10,
            accountMode: UserChatActiveTaskModes.Queue,
            messageMode: UserChatActiveTaskModes.Queue);

        var send = Assert.Single(plan);
        Assert.Equal(0, send.AccountIndex);
        Assert.Equal(0, send.MessageIndex);
    }

    [Fact]
    public void 有限任务总数跟随实际计划数封顶()
    {
        var plan = UserChatActiveSendPlanner.BuildFiniteRunPlan(
            eligibleAccountCount: 1,
            requestedMessageCount: 10,
            dictionaryCount: 10,
            accountMode: UserChatActiveTaskModes.Queue,
            messageMode: UserChatActiveTaskModes.Queue);

        var total = UserChatActiveSendPlanner.ResolveFiniteRunTotal(completedMessageCount: 0, plannedSendCount: plan.Count);

        Assert.Equal(1, total);
    }


    [Fact]
    public void 有限任务账号足够时每个账号最多分配一条消息()
    {
        var plan = UserChatActiveSendPlanner.BuildFiniteRunPlan(
            eligibleAccountCount: 10,
            requestedMessageCount: 10,
            dictionaryCount: 10,
            accountMode: UserChatActiveTaskModes.Queue,
            messageMode: UserChatActiveTaskModes.Queue);

        Assert.Equal(10, plan.Count);
        Assert.Equal(Enumerable.Range(0, 10), plan.Select(x => x.AccountIndex));
        Assert.Equal(Enumerable.Range(0, 10), plan.Select(x => x.MessageIndex));
        Assert.Equal(plan.Count, plan.Select(x => x.AccountIndex).Distinct().Count());
    }
}
