using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doggo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCoinVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "UserCoins",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "UserCoins");
        }
    }
}
