using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramPanel.Core.Services;
using TelegramPanel.Data;
using TelegramPanel.Data.Entities;
using TelegramPanel.Data.Repositories;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class BatchTaskNamePersistenceTests
{
    [Fact]
    public async Task EditableDraftUpdate_canRenameAndClearBatchTaskName()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        int taskId;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.BatchTasks.Add(new BatchTask
            {
                Name = "旧名称",
                TaskType = "user_chat_active",
                Status = "completed",
                Total = 1,
                Completed = 1,
                Config = "{\"targets\":[]}",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            taskId = db.BatchTasks.Single().Id;
        }

        await using (var db = new AppDbContext(options))
        {
            var service = CreateService(db);
            var renamed = await service.TryUpdateEditableTaskDraftAsync(
                taskId,
                total: 2,
                config: "{\"targets\":[\"@demo\"]}",
                name: "新任务名称");

            Assert.True(renamed);
        }

        await using (var db = new AppDbContext(options))
        {
            var renamedTask = await db.BatchTasks.AsNoTracking().SingleAsync();
            Assert.Equal("新任务名称", renamedTask.Name);
            Assert.Equal(2, renamedTask.Total);
            Assert.Equal("{\"targets\":[\"@demo\"]}", renamedTask.Config);
        }

        await using (var db = new AppDbContext(options))
        {
            var service = CreateService(db);
            var cleared = await service.TryUpdateEditableTaskDraftAsync(
                taskId,
                total: 3,
                config: null,
                name: null);

            Assert.True(cleared);
        }

        await using (var db = new AppDbContext(options))
        {
            var clearedTask = await db.BatchTasks.AsNoTracking().SingleAsync();
            Assert.Null(clearedTask.Name);
            Assert.Equal(3, clearedTask.Total);
            Assert.Null(clearedTask.Config);
        }
    }

    private static BatchTaskManagementService CreateService(AppDbContext db)
    {
        return new BatchTaskManagementService(
            new BatchTaskRepository(db),
            new ConfigurationBuilder().Build(),
            NullLogger<BatchTaskManagementService>.Instance);
    }
}
