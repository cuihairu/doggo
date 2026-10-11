using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doggo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarketItemUniqueKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MarketItems_ClassId",
                table: "MarketItems");

            migrationBuilder.AlterColumn<string>(
                name: "MarketHashName",
                table: "MarketItems",
                type: "varchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "IconUrl",
                table: "MarketItems",
                type: "varchar(2048)",
                maxLength: 2048,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "ClassId",
                table: "MarketItems",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldMaxLength: 255);

            migrationBuilder.CreateIndex(
                name: "IX_MarketItems_Game_ClassId",
                table: "MarketItems",
                columns: new[] { "Game", "ClassId" },
                unique: true,
                filter: "`ClassId` <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_MarketItems_Game_MarketHashName",
                table: "MarketItems",
                columns: new[] { "Game", "MarketHashName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MarketItems_Game_ClassId",
                table: "MarketItems");

            migrationBuilder.DropIndex(
                name: "IX_MarketItems_Game_MarketHashName",
                table: "MarketItems");

            migrationBuilder.AlterColumn<string>(
                name: "MarketHashName",
                table: "MarketItems",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "IconUrl",
                table: "MarketItems",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(2048)",
                oldMaxLength: 2048);

            migrationBuilder.AlterColumn<string>(
                name: "ClassId",
                table: "MarketItems",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64);

            migrationBuilder.CreateIndex(
                name: "IX_MarketItems_ClassId",
                table: "MarketItems",
                column: "ClassId",
                unique: true);
        }
    }
}
