using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Models;
using TelegramPanel.Core.Services.Proxy;
using TelegramPanel.Data;
using TelegramPanel.Data.Entities;
using WTelegram;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class ProxyEgressMaintenanceTests
{
    [Fact]
    public async Task 巡检只刷新启用的普通和Resin代理并更新出口元数据()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var manual = NewProxy("manual", OutboundProxyKinds.Manual, 1080);
        var resin = NewProxy("resin", OutboundProxyKinds.Resin, 1081);
        var disabled = NewProxy("disabled", OutboundProxyKinds.Manual, 1082);
        disabled.IsEnabled = false;
        var warp = NewProxy("warp", OutboundProxyKinds.Warp, 1083);
        var mtProxy = NewProxy("mtproxy", OutboundProxyKinds.Manual, 1084);
        mtProxy.Protocol = OutboundProxyProtocols.MtProto;
        db.OutboundProxies.AddRange(manual, resin, disabled, warp, mtProxy);
        await db.SaveChangesAsync();

        var probe = new RecordingProbeService();
        var configuration = new ConfigurationBuilder().Build();
        var service = new ProxyManagementService(
            db,
            new EmptyClientPool(),
            probe,
            new WarpContainerManager(
                db,
                configuration,
                probe,
                NullLogger<WarpContainerManager>.Instance),
            NullLogger<ProxyManagementService>.Instance,
            configuration);

        var result = await service.RefreshAllNonWarpProxyEgressAsync();

        Assert.Equal(2, result.Checked);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(
            new[] { manual.Id, resin.Id },
            result.Items.Select(x => x.ProxyId).ToArray());
        Assert.Equal(2, probe.ProxyIds.Count);

        db.ChangeTracker.Clear();
        var refreshed = await db.OutboundProxies.AsNoTracking().ToDictionaryAsync(x => x.Id);
        Assert.Equal($"203.0.113.{manual.Id}", refreshed[manual.Id].EgressIp);
        Assert.Equal($"203.0.113.{resin.Id}", refreshed[resin.Id].EgressIp);
        Assert.Null(refreshed[disabled.Id].LastTestedAtUtc);
        Assert.Null(refreshed[warp.Id].LastTestedAtUtc);
        Assert.Null(refreshed[mtProxy.Id].LastTestedAtUtc);
    }

    [Fact]
    public void 出口巡检默认每五分钟执行且支持配置关闭()
    {
        var defaults = ProxyEgressMaintenanceOptions.From(new ConfigurationBuilder().Build());
        Assert.True(defaults.Enabled);
        Assert.Equal(5, defaults.IntervalMinutes);

        var disabled = ProxyEgressMaintenanceOptions.From(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Proxy:Egress:Maintenance:Enabled"] = "false",
                    ["Proxy:Egress:Maintenance:IntervalMinutes"] = "15"
                })
                .Build());
        Assert.False(disabled.Enabled);
        Assert.Equal(15, disabled.IntervalMinutes);
    }

    private static OutboundProxy NewProxy(string name, string kind, int port) => new()
    {
        Name = name,
        Kind = kind,
        Protocol = OutboundProxyProtocols.Socks5,
        Host = "127.0.0.1",
        Port = port,
        Password = kind == OutboundProxyKinds.Resin ? "proxy-token" : null,
        IsEnabled = true,
        TestStatus = "unknown"
    };

    private sealed class RecordingProbeService : IProxyEgressProbeService
    {
        public List<int> ProxyIds { get; } = [];

        public Task<EgressProbeResult> ProbePanelAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EgressProbeResult> ProbeProxyAsync(
            OutboundProxy proxy,
            string stableAccountKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProxyIds.Add(proxy.Id);
            return Task.FromResult(new EgressProbeResult(
                true,
                $"203.0.113.{proxy.Id}",
                "CN",
                "Shanghai",
                "Example ISP",
                null,
                20,
                DateTime.UtcNow,
                null));
        }

        public Task<EgressProbeResult> ProbeProxyAsync(
            ProxyConnectionOptions options,
            bool requireWarp = false,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyClientPool : ITelegramClientPool
    {
        public int ActiveClientCount => 0;

        public Task<Client> GetOrCreateClientAsync(
            int accountId,
            int apiId,
            string apiHash,
            string sessionPath,
            string? sessionKey = null,
            string? phoneNumber = null,
            long? userId = null) => throw new NotSupportedException();

        public Client? GetClient(int accountId) => null;
        public Task RemoveClientAsync(int accountId) => Task.CompletedTask;
        public Task RemoveAllClientsAsync() => Task.CompletedTask;
        public bool IsClientConnected(int accountId) => false;
    }
}
