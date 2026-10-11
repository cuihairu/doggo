using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doggo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CasePrizeIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserPrizes_OrderId_AssetId",
                table: "UserPrizes",
                columns: new[] { "OrderId", "AssetId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPrizes_OrderId_AssetId",
                table: "UserPrizes");
        }
    }
}
