using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace idnetityServiceWedApi.Data.Migrations;

[DbContext(typeof(AuthDbContext))]
[Migration("20260801000000_AddOutboxDeadLetter")]
public partial class AddOutboxDeadLetter : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DeadLetteredAt",
            schema: "IAM",
            table: "OutboxMessage",
            type: "datetimeoffset",
            nullable: true);

        // Supports the dispatcher's hot path: undispatched, not dead-lettered, oldest first.
        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessage_Pending",
            schema: "IAM",
            table: "OutboxMessage",
            columns: ["DispatchedAt", "DeadLetteredAt", "CreatedAt"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_OutboxMessage_Pending", schema: "IAM", table: "OutboxMessage");
        migrationBuilder.DropColumn(name: "DeadLetteredAt", schema: "IAM", table: "OutboxMessage");
    }
}
