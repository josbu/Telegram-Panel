using System.Net.Sockets;
using TL;

namespace TelegramPanel.Core.Services.Telegram;

/// <summary>
/// 对 Telegram 瞬时连接故障执行一次客户端重建和重试。
/// </summary>
internal static class TelegramTransientConnectionRetry
{
    private static readonly string[] TransientMessageMarkers =
    [
        "A task was canceled",
        "operation was canceled",
        "connection reset",
        "connection closed",
        "connection aborted",
        "connection refused",
        "connection shut down",
        "forcibly closed",
        "transport connection",
        "SocketException",
        "ObjectDisposedException",
        "unexpected EOF",
        "连接失败",
        "连接已关闭",
        "连接被关闭",
        "远程主机强迫关闭",
        "代理异常"
    ];

    internal static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        Func<Task> resetClient,
        CancellationToken cancellationToken,
        Action<Exception>? onRetry = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(resetClient);

        try
        {
            return await operation();
        }
        catch (Exception ex) when (ShouldRetry(ex, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            onRetry?.Invoke(ex);
            await resetClient();
            cancellationToken.ThrowIfCancellationRequested();
        }

        try
        {
            return await operation();
        }
        catch (Exception ex) when (ShouldRetry(ex, cancellationToken))
        {
            // 第二次失败不再重试，但要清理新建的故障客户端，避免后续请求继续复用。
            try
            {
                await resetClient();
            }
            catch
            {
                // 清理失败不能覆盖原始 Telegram 连接异常。
            }

            throw;
        }
    }

    internal static bool ShouldRetry(Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (cancellationToken.IsCancellationRequested)
            return false;

        var exceptions = EnumerateExceptions(exception).ToList();
        if (exceptions.Any(item => item is RpcException))
            return false;

        if (exceptions.Any(item => item is TimeoutException
                                   or TaskCanceledException
                                   or OperationCanceledException
                                   or IOException
                                   or SocketException
                                   or HttpRequestException
                                   or ObjectDisposedException))
        {
            return true;
        }

        return exceptions.Any(item => TransientMessageMarkers.Any(marker =>
            (item.Message ?? string.Empty).Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }

    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        yield return exception;

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions.SelectMany(EnumerateExceptions))
                yield return inner;
            yield break;
        }

        if (exception.InnerException != null)
        {
            foreach (var inner in EnumerateExceptions(exception.InnerException))
                yield return inner;
        }
    }
}
