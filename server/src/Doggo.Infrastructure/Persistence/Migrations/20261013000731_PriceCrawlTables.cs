using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doggo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PriceCrawlTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PriceCrawlItemMaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    SiteKey = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    ExternalId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    ExternalTitle = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    ExternalUrl = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    MarketItemId = table.Column<Guid>(type: "char(36)", nullable: true),
                    MapStatus = table.Column<int>(type: "int", nullable: false),
                    MatchSource = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceCrawlItemMaps", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PriceCrawlRaws",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    SiteKey = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    ExternalId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    PriceCents = table.Column<long>(type: "bigint", nullable: true),
                    CurrencyCode = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    RawPriceText = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    PageFingerprint = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FetchedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AdapterVersion = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceCrawlRaws", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PriceCrawlSeriesPoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    MapId = table.Column<Guid>(type: "char(36)", nullable: false),
                    MarketItemId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SiteKey = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    ExternalId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    PriceCents = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyCode = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    ConvertedCents = table.Column<long>(type: "bigint", nullable: true),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ObservedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    WindowKey = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    IsSuspect = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceCrawlSeriesPoints", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PriceCrawlSiteStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    SiteKey = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    LastRunAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastDiscoverAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false),
                    CircuitOpenUntilUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RobotsCrawlDelaySeconds = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceCrawlSiteStates", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PriceCrawlItemMaps_MapStatus_SiteKey",
                table: "PriceCrawlItemMaps",
                columns: new[] { "MapStatus", "SiteKey" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceCrawlItemMaps_MarketItemId",
                table: "PriceCrawlItemMaps",
                column: "MarketItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceCrawlItemMaps_SiteKey_ExternalId",
                table: "PriceCrawlItemMaps",
                columns: new[] { "SiteKey", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceCrawlRaws_FetchedAtUtc",
                table: "PriceCrawlRaws",
                column: "FetchedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PriceCrawlRaws_SiteKey_ExternalId_ObservedAtUtc",
                table: "PriceCrawlRaws",
                columns: new[] { "SiteKey", "ExternalId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceCrawlSeriesPoints_MapId_WindowKey",
                table: "PriceCrawlSeriesPoints",
                columns: new[] { "MapId", "WindowKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceCrawlSeriesPoints_MarketItemId_ObservedAtUtc",
                table: "PriceCrawlSeriesPoints",
                columns: new[] { "MarketItemId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceCrawlSiteStates_SiteKey",
                table: "PriceCrawlSiteStates",
                column: "SiteKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceCrawlItemMaps");

            migrationBuilder.DropTable(
                name: "PriceCrawlRaws");

            migrationBuilder.DropTable(
                name: "PriceCrawlSeriesPoints");

            migrationBuilder.DropTable(
                name: "PriceCrawlSiteStates");
        }
    }
}
