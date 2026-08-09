using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TelegramPanel.Data;
using TelegramPanel.Data.Entities;
using TelegramPanel.Data.Repositories;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class AccountRepositoryWasteFilterTests
{
    [Fact]
    public async Task 临时或不确定状态不进入废号筛选且会自动复查()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Accounts.AddRange(
            Account("8613800000001", "连接失败"),
            Account("8613800000002", "请求超时"),
            Account("8613800000003", "刷新失败"),
            Account("8613800000004", "创建频道探测失败"),
            Account("8613800000005", "无法获取账号资料"),
            Account("8613800000006", "Session 失效（AUTH_KEY_UNREGISTERED）"));
        await db.SaveChangesAsync();

        var repository = new AccountRepository(db);
        var (items, total) = await repository.QueryPagedAsync(
            categoryId: null,
            search: null,
            pageIndex: 0,
            pageSize: 20,
            onlyWaste: true);

        var item = Assert.Single(items);
        Assert.Equal(1, total);
        Assert.Contains("Session 失效", item.TelegramStatusSummary);

        var refreshItems = await repository.GetTransientFailedStatusAccountsAsync(
            count: 20,
            minAge: TimeSpan.Zero);

        Assert.Equal(
            [
                "8613800000001",
                "8613800000002",
                "8613800000003",
                "8613800000004",
                "8613800000005"
            ],
            refreshItems.Select(account => account.Phone).OrderBy(phone => phone).ToArray());
    }

    private static Account Account(string phone, string summary) => new()
    {
        Phone = phone,
        UserId = long.Parse(phone),
        SessionPath = $"sessions/{phone}.session",
        ApiId = 1,
        ApiHash = "0123456789abcdef0123456789abcdef",
        TelegramStatusOk = false,
        TelegramStatusSummary = summary,
        TelegramStatusCheckedAtUtc = DateTime.UtcNow
    };
}
