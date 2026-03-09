using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using TelegramPanel.Modules;

namespace TelegramPanel.Modules.MessageReport;

public sealed class MessageReportTaskModule : ITelegramPanelModule, IModuleTaskProvider
{
    public ModuleManifest Manifest { get; } = new()
    {
        Id = "task.message-report",
        Name = "任务：消息举报",
        Version = "1.0.0",
        Host = new HostCompatibility { Min = "0.0.0", Max = "2.0.0" },
        Entry = new ModuleEntryPoint
        {
            Assembly = "TelegramPanel.Modules.MessageReport.dll",
            Type = typeof(MessageReportTaskModule).FullName ?? string.Empty
        }
    };

    public void ConfigureServices(IServiceCollection services, ModuleHostContext context)
    {
        services.AddScoped<MessageReportTaskTelegramService>();
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
            TaskType = MessageReportTaskTypes.TaskType,
            DisplayName = "消息举报（多账号）",
            Description = "按账号分类持续举报指定消息，支持间隔控制、达到次数后停止，也支持 Cron 计划触发。",
            Icon = Icons.Material.Filled.Warning,
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
            Order = 160
        };
    }
}
