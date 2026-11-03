using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doggo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketPriceDailyCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MarketPriceDailies_MarketItemId_Date",
                table: "MarketPriceDailies");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "MarketPriceDailies",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceDailies_MarketItemId_Date_Currency",
                table: "MarketPriceDailies",
                columns: new[] { "MarketItemId", "Date", "Currency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MarketPriceDailies_MarketItemId_Date_Currency",
                table: "MarketPriceDailies");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "MarketPriceDailies");

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceDailies_MarketItemId_Date",
                table: "MarketPriceDailies",
                columns: new[] { "MarketItemId", "Date" },
                unique: true);
        }
    }
}
