using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doggo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnalyticsFunnelTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalyticsEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    EventName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SessionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    VisitorKey = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    Client = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    PropertiesJson = table.Column<string>(type: "longtext", maxLength: 255, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    Path = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsEvents", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FunnelDailyStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    Client = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    ViewProductUv = table.Column<long>(type: "bigint", nullable: false),
                    CheckoutIntentUv = table.Column<long>(type: "bigint", nullable: false),
                    OrderCreatedUv = table.Column<long>(type: "bigint", nullable: false),
                    OrderPaidUv = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunnelDailyStats", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_EventName_OccurredAtUtc",
                table: "AnalyticsEvents",
                columns: new[] { "EventName", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_IdempotencyKey",
                table: "AnalyticsEvents",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_SessionId_OccurredAtUtc",
                table: "AnalyticsEvents",
                columns: new[] { "SessionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_VisitorKey_OccurredAtUtc",
                table: "AnalyticsEvents",
                columns: new[] { "VisitorKey", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FunnelDailyStats_Date_Client_Channel",
                table: "FunnelDailyStats",
                columns: new[] { "Date", "Client", "Channel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsEvents");

            migrationBuilder.DropTable(
                name: "FunnelDailyStats");
        }
    }
}
