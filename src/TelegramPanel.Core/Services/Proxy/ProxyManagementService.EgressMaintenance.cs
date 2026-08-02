using Microsoft.EntityFrameworkCore;
using TelegramPanel.Core.Models;

namespace TelegramPanel.Core.Services.Proxy;

public sealed partial class ProxyManagementService
{
    /// <summary>
    /// 刷新所有启用的普通代理和 Resin 代理出口。
    /// 受管 WARP 由专用维护服务处理，避免与容器恢复和首次连接冻结保护发生竞争。
    /// </summary>
    public async Task<ProxyEgressMaintenanceBatchResult> RefreshAllNonWarpProxyEgressAsync(
        CancellationToken cancellationToken = default)
    {
        var proxies = await _db.OutboundProxies
            .AsNoTracking()
            .Where(x => x.IsEnabled
                && x.Kind != OutboundProxyKinds.Warp
                && x.Protocol != OutboundProxyProtocols.MtProto)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        var items = new List<ProxyEgressMaintenanceItem>(proxies.Count);
        foreach (var candidate in proxies)
        {
            try
            {
                var proxy = await TestAsync(candidate.Id, cancellationToken);
                items.Add(new ProxyEgressMaintenanceItem(
                    proxy.Id,
                    proxy.Name,
                    proxy.TestStatus == "ok",
                    proxy.EgressIp,
                    proxy.LastError));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                items.Add(new ProxyEgressMaintenanceItem(
                    candidate.Id,
                    candidate.Name,
                    false,
                    null,
                    SafeError(ex)));
            }
        }

        return new ProxyEgressMaintenanceBatchResult(
            items.Count,
            items.Count(x => x.Success),
            items.Count(x => !x.Success),
            items);
    }
}
