using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.OmnichannelOutbox.Domain;

namespace Nop.Plugin.Misc.OmnichannelOutbox.Data;

[NopMigration("2026/05/23 12:00:00", "Misc.OmnichannelOutbox base schema", MigrationProcessType.Installation)]
public class SchemaMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists<OutboxMessage>();
    }

    public override void Down()
    {
        this.DeleteTableIfExists<OutboxMessage>();
    }
}
