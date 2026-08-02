using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace idnetityServiceWedApi.Data.Migrations;

[DbContext(typeof(AuthDbContext))]
[Migration("20260802000000_AddOutboxLocking")]
public partial class AddOutboxLocking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LockedUntil",
            schema: "IAM",
            table: "OutboxMessage",
            type: "datetimeoffset",
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "LockedUntil", schema: "IAM", table: "OutboxMessage");
}
