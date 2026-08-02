using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace idnetityServiceWedApi.Data.Migrations;

[DbContext(typeof(AuthDbContext))]
[Migration("20260802000001_AddOutboxLockOwnership")]
public sealed class AddOutboxLockOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "LockId",
            schema: "IAM",
            table: "OutboxMessage",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.DropIndex(
            name: "IX_OutboxMessage_Pending",
            schema: "IAM",
            table: "OutboxMessage");

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessage_Pending",
            schema: "IAM",
            table: "OutboxMessage",
            columns: ["DispatchedAt", "DeadLetteredAt", "LockedUntil", "CreatedAt"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OutboxMessage_Pending",
            schema: "IAM",
            table: "OutboxMessage");

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessage_Pending",
            schema: "IAM",
            table: "OutboxMessage",
            columns: ["DispatchedAt", "DeadLetteredAt", "CreatedAt"]);

        migrationBuilder.DropColumn(name: "LockId", schema: "IAM", table: "OutboxMessage");
    }
}
