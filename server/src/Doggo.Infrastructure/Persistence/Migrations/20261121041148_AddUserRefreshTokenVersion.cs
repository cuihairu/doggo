using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doggo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRefreshTokenVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "UserRefreshTokens",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "UserRefreshTokens");
        }
    }
}
