using TelegramPanel.Web.Services;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class DataSyncServiceCancellationTests
{
    [Fact]
    public void AccountRequestCancellation_DoesNotInvalidateAccount()
    {
        var exception = new TaskCanceledException("A task was canceled.");

        Assert.True(DataSyncService.IsTransientRequestCancellation(exception, CancellationToken.None));
    }

    [Fact]
    public void SyncCancellation_IsHandledByTaskRunner()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = new OperationCanceledException(cancellation.Token);

        Assert.False(DataSyncService.IsTransientRequestCancellation(exception, cancellation.Token));
    }

    [Fact]
    public void NonCancellationFailure_StillUpdatesAccountStatus()
    {
        Assert.False(DataSyncService.IsTransientRequestCancellation(
            new TimeoutException("Telegram 请求超时"),
            CancellationToken.None));
    }
}
