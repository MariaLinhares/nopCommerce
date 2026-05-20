using Nop.Services.Common;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.OmnichannelOutbox;

/// <summary>
/// Omnichannel Outbox plugin — installs/uninstalls the dispatcher and retention schedule tasks
/// </summary>
public class OmnichannelOutboxPlugin : BasePlugin, IMiscPlugin
{
    private readonly IScheduleTaskService _scheduleTaskService;

    public OmnichannelOutboxPlugin(IScheduleTaskService scheduleTaskService)
    {
        _scheduleTaskService = scheduleTaskService;
    }

    public override async Task InstallAsync()
    {
        // Install dispatcher task (polls outbox every 10s)
        if (await _scheduleTaskService.GetTaskByTypeAsync(OmnichannelOutboxDefaults.DispatcherTask.Type) is null)
        {
            await _scheduleTaskService.InsertTaskAsync(new()
            {
                Enabled = true,
                StopOnError = false,
                LastEnabledUtc = DateTime.UtcNow,
                Name = OmnichannelOutboxDefaults.DispatcherTask.Name,
                Type = OmnichannelOutboxDefaults.DispatcherTask.Type,
                Seconds = OmnichannelOutboxDefaults.DispatcherTask.Period
            });
        }

        // Install retention task (cleans up dispatched rows daily)
        if (await _scheduleTaskService.GetTaskByTypeAsync(OmnichannelOutboxDefaults.RetentionTask.Type) is null)
        {
            await _scheduleTaskService.InsertTaskAsync(new()
            {
                Enabled = true,
                StopOnError = false,
                LastEnabledUtc = DateTime.UtcNow,
                Name = OmnichannelOutboxDefaults.RetentionTask.Name,
                Type = OmnichannelOutboxDefaults.RetentionTask.Type,
                Seconds = OmnichannelOutboxDefaults.RetentionTask.Period
            });
        }

        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        // Remove dispatcher task
        var dispatcherTask = await _scheduleTaskService.GetTaskByTypeAsync(OmnichannelOutboxDefaults.DispatcherTask.Type);
        if (dispatcherTask is not null)
            await _scheduleTaskService.DeleteTaskAsync(dispatcherTask);

        // Remove retention task
        var retentionTask = await _scheduleTaskService.GetTaskByTypeAsync(OmnichannelOutboxDefaults.RetentionTask.Type);
        if (retentionTask is not null)
            await _scheduleTaskService.DeleteTaskAsync(retentionTask);

        await base.UninstallAsync();
    }
}
