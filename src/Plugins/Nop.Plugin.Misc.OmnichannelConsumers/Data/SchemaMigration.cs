using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.OmnichannelConsumers.Domain;

namespace Nop.Plugin.Misc.OmnichannelConsumers.Data;

[NopMigration("2026/05/23 13:00:00", "Misc.OmnichannelConsumers base schema", MigrationProcessType.Installation)]
public class SchemaMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists<ProcessedInboxMessage>();

        if (!Schema.Table(nameof(ProcessedInboxMessage)).Index("IX_ProcessedInboxMessage_EventId").Exists())
        {
            Create.Index("IX_ProcessedInboxMessage_EventId")
                .OnTable(nameof(ProcessedInboxMessage))
                .OnColumn(nameof(ProcessedInboxMessage.EventId))
                .Unique();
        }
    }

    public override void Down()
    {
        this.DeleteTableIfExists<ProcessedInboxMessage>();
    }
}
