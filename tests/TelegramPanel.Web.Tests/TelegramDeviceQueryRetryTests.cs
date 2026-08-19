using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TelegramPanel.Core.Models;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Web.Api;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class TelegramDeviceQueryRetryTests
{
    [Fact]
    public async Task 瞬时连接失败会重建客户端并重试一次()
    {
        var attempts = 0;
        var resets = 0;

        var result = await TelegramTransientConnectionRetry.ExecuteAsync(
            () => ++attempts == 1
                ? Task.FromException<int>(new SocketException((int)SocketError.ConnectionReset))
                : Task.FromResult(43),
            () =>
            {
                resets++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(43, result);
        Assert.Equal(2, attempts);
        Assert.Equal(1, resets);
    }

    [Theory]
    [InlineData("task")]
    [InlineData("operation")]
    public async Task 非调用方取消会重建客户端并重试一次(string cancellationKind)
    {
        var attempts = 0;
        var resets = 0;

        var result = await TelegramTransientConnectionRetry.ExecuteAsync(
            () => ++attempts == 1
                ? Task.FromException<int>(cancellationKind == "task"
                    ? new TaskCanceledException("A task was canceled.")
                    : new OperationCanceledException("The operation was canceled."))
                : Task.FromResult(54),
            () =>
            {
                resets++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(54, result);
        Assert.Equal(2, attempts);
        Assert.Equal(1, resets);
    }

    [Fact]
    public void IO故障会识别为瞬时连接错误()
    {
        Assert.True(TelegramTransientConnectionRetry.ShouldRetry(
            new IOException("unexpected EOF"),
            CancellationToken.None));
    }

    [Fact]
    public void 会话加载包装异常中的取消会识别为瞬时故障()
    {
        var error = new InvalidOperationException(
            "Telegram 会话加载失败",
            new TaskCanceledException("A task was canceled."));

        Assert.True(TelegramTransientConnectionRetry.ShouldRetry(
            error,
            CancellationToken.None));
    }

    [Fact]
    public void WTelegram连接关闭错误会识别为瞬时故障()
    {
        var error = new WTelegram.WTException(
            "Could not read payload length : Connection shut down");

        Assert.True(TelegramTransientConnectionRetry.ShouldRetry(
            error,
            CancellationToken.None));
    }

    [Fact]
    public async Task Telegram业务错误不会重建或重试()
    {
        var attempts = 0;
        var resets = 0;
        var error = new TL.RpcException(420, "FLOOD_WAIT_30");

        var thrown = await Assert.ThrowsAsync<TL.RpcException>(() =>
            TelegramTransientConnectionRetry.ExecuteAsync<int>(
                () =>
                {
                    attempts++;
                    return Task.FromException<int>(error);
                },
                () =>
                {
                    resets++;
                    return Task.CompletedTask;
                },
                CancellationToken.None));

        Assert.Same(error, thrown);
        Assert.Equal(1, attempts);
        Assert.Equal(0, resets);
    }

    [Theory]
    [InlineData("Session 失效（AUTH_KEY_UNREGISTERED）")]
    [InlineData("账号权限不足：CHAT_WRITE_FORBIDDEN")]
    public async Task Session和权限错误不会重建或重试(string message)
    {
        var attempts = 0;
        var resets = 0;
        var error = new InvalidOperationException(message);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TelegramTransientConnectionRetry.ExecuteAsync<int>(
                () =>
                {
                    attempts++;
                    return Task.FromException<int>(error);
                },
                () =>
                {
                    resets++;
                    return Task.CompletedTask;
                },
                CancellationToken.None));

        Assert.Same(error, thrown);
        Assert.Equal(1, attempts);
        Assert.Equal(0, resets);
    }

    [Fact]
    public async Task 调用方取消时不会重建或重试()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var attempts = 0;
        var resets = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TelegramTransientConnectionRetry.ExecuteAsync<int>(
                () =>
                {
                    attempts++;
                    return Task.FromCanceled<int>(cancellation.Token);
                },
                () =>
                {
                    resets++;
                    return Task.CompletedTask;
                },
                cancellation.Token));

        Assert.Equal(1, attempts);
        Assert.Equal(0, resets);
    }

    [Fact]
    public async Task 第二次仍失败时只重试一次并清理故障客户端()
    {
        var attempts = 0;
        var resets = 0;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            TelegramTransientConnectionRetry.ExecuteAsync<int>(
                () =>
                {
                    attempts++;
                    return Task.FromException<int>(new HttpRequestException("proxy connection closed"));
                },
                () =>
                {
                    resets++;
                    return Task.CompletedTask;
                },
                CancellationToken.None));

        Assert.Equal(2, attempts);
        Assert.Equal(2, resets);
    }

    [Fact]
    public async Task 在线设备最终失败返回502和可读中文消息()
    {
        var context = CreateHttpContext();
        var result = PanelAdminApiEndpoints.CreateDeviceQueryFailure(
            new HttpRequestException("proxy connection closed"));

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        var payload = await ReadJsonAsync(context);
        Assert.False(payload.GetProperty("success").GetBoolean());
        Assert.Contains("在线设备读取失败", payload.GetProperty("message").GetString());
        Assert.Equal(
            "TELEGRAM_DEVICE_QUERY_FAILED",
            payload.GetProperty("code").GetString());
    }

    [Fact]
    public async Task 在线设备正常结果保持200数组响应()
    {
        var context = CreateHttpContext();
        var result = PanelAdminApiEndpoints.CreateDeviceQuerySuccess(
            Array.Empty<TelegramAuthorizationInfo>());

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var payload = await ReadJsonAsync(context);
        Assert.Equal(JsonValueKind.Array, payload.ValueKind);
        Assert.Empty(payload.EnumerateArray());
    }

    [Fact]
    public async Task 在线设备响应将长授权哈希序列化为字符串()
    {
        var context = CreateHttpContext();
        var result = PanelAdminApiEndpoints.CreateDeviceQuerySuccess(
            new[]
            {
                new TelegramAuthorizationInfo(
                    Hash: 9007199254740993L,
                    Current: false,
                    ApiId: 6,
                    AppName: "Telegram",
                    AppVersion: "12.7.3",
                    DeviceModel: "Samsung SM-G991B",
                    Platform: "Android",
                    SystemVersion: "Android 14",
                    Ip: null,
                    Country: null,
                    Region: null,
                    CreatedAtUtc: null,
                    LastActiveAtUtc: null)
            });

        await result.ExecuteAsync(context);

        var payload = await ReadJsonAsync(context);
        var device = payload[0];
        Assert.Equal(JsonValueKind.String, device.GetProperty("hash").ValueKind);
        Assert.Equal("9007199254740993", device.GetProperty("hash").GetString());
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddRouting()
            .BuildServiceProvider();
        return new DefaultHttpContext
        {
            RequestServices = services,
            Response =
            {
                Body = new MemoryStream()
            }
        };
    }

    private static async Task<JsonElement> ReadJsonAsync(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
    }
}
