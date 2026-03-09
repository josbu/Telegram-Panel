using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using TelegramPanel.Modules;
using TelegramPanel.Modules.MessageReportTask.Components.Dialogs;
using TelegramPanel.Modules.MessageReportTask.Services;

namespace TelegramPanel.Modules.MessageReportTask;

public sealed class MessageReportTaskModule : ITelegramPanelModule, IModuleTaskProvider
{
    public ModuleManifest Manifest { get; } = new()
    {
        Id = MessageReportTaskConstants.ModuleId,
        Name = "任务：消息举报",
        Version = "1.0.0",
        Host = new HostCompatibility { Min = "1.31.6", Max = "2.0.0" },
        Entry = new ModuleEntryPoint
        {
            Assembly = "TelegramPanel.Modules.MessageReportTask.dll",
            Type = typeof(MessageReportTaskModule).FullName!
        }
    };

    public void ConfigureServices(IServiceCollection services, ModuleHostContext context)
    {
        services.AddScoped<TelegramMessageReportService>();
        services.AddScoped<IModuleTaskHandler, MessageReportTaskHandler>();
        services.AddSingleton<IModuleTaskRerunBuilder, MessageReportTaskRerunBuilder>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ModuleHostContext context)
    {
    }

    public IEnumerable<ModuleTaskDefinition> GetTasks(ModuleHostContext context)
    {
        yield return new ModuleTaskDefinition
        {
            Category = "user",
            TaskType = MessageReportTaskConstants.TaskType,
            DisplayName = "消息举报（多账号）",
            Description = "按账号分类对指定消息链接执行举报，支持间隔、Cron 计划和数量上限。",
            Icon = Icons.Material.Filled.Flag,
            EditorComponentType = typeof(MessageReportTaskEditor).AssemblyQualifiedName ?? string.Empty,
            TaskCenter = new ModuleTaskCenterCapabilities
            {
                CanPause = true,
                CanResume = true,
                CanEdit = true,
                CanRerun = true,
                EditComponentType = typeof(MessageReportTaskEditor).AssemblyQualifiedName ?? string.Empty,
                AutoPauseBeforeEdit = true
            },
            Order = 150
        };
    }
}
