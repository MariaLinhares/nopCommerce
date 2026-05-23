using Nop.Services.Common;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.OmnichannelConsumers;

/// <summary>
/// Consumers plugin — registers/unregisters the RabbitMQ subscriber schedule task and
/// the inbox retention task. Mirrors OmnichannelOutboxPlugin in shape so the install
/// story is symmetric for the demo.
/// </summary>
public class OmnichannelConsumersPlugin : BasePlugin, IMiscPlugin
{
    private readonly IScheduleTaskService _scheduleTaskService;

    public OmnichannelConsumersPlugin(IScheduleTaskService scheduleTaskService)
    {
        _scheduleTaskService = scheduleTaskService;
    }

    public override async Task InstallAsync()
    {
        if (await _scheduleTaskService.GetTaskByTypeAsync(OmnichannelConsumersDefaults.SubscriberTask.Type) is null)
        {
            await _scheduleTaskService.InsertTaskAsync(new()
            {
                Enabled = true,
                StopOnError = false,
                LastEnabledUtc = DateTime.UtcNow,
                Name = OmnichannelConsumersDefaults.SubscriberTask.Name,
                Type = OmnichannelConsumersDefaults.SubscriberTask.Type,
                Seconds = OmnichannelConsumersDefaults.SubscriberTask.Period
            });
        }

        if (await _scheduleTaskService.GetTaskByTypeAsync(OmnichannelConsumersDefaults.InboxRetentionTask.Type) is null)
        {
            await _scheduleTaskService.InsertTaskAsync(new()
            {
                Enabled = true,
                StopOnError = false,
                LastEnabledUtc = DateTime.UtcNow,
                Name = OmnichannelConsumersDefaults.InboxRetentionTask.Name,
                Type = OmnichannelConsumersDefaults.InboxRetentionTask.Type,
                Seconds = OmnichannelConsumersDefaults.InboxRetentionTask.Period
            });
        }

        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        var subscriberTask = await _scheduleTaskService.GetTaskByTypeAsync(OmnichannelConsumersDefaults.SubscriberTask.Type);
        if (subscriberTask is not null)
            await _scheduleTaskService.DeleteTaskAsync(subscriberTask);

        var retentionTask = await _scheduleTaskService.GetTaskByTypeAsync(OmnichannelConsumersDefaults.InboxRetentionTask.Type);
        if (retentionTask is not null)
            await _scheduleTaskService.DeleteTaskAsync(retentionTask);

        await base.UninstallAsync();
    }
}
