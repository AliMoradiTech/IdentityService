using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace idnetityServiceWedApi.Data.Migrations;

[DbContext(typeof(AuthDbContext))]
[Migration("20260730000000_AddOutbox")]
public partial class AddOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OutboxMessage",
            schema: "IAM",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                TraceParent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                TraceState = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                DispatchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                Attempts = table.Column<int>(type: "int", nullable: false),
                Error = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_OutboxMessage", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessage_EventId",
            schema: "IAM",
            table: "OutboxMessage",
            column: "EventId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "OutboxMessage", schema: "IAM");
    }
}
