using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doggo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccountPhoneEmailUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailUnique",
                table: "UserAccounts",
                type: "varchar(255)",
                nullable: true,
                computedColumnSql: "NULLIF(`Email`, '')",
                stored: false);

            migrationBuilder.AddColumn<string>(
                name: "PhoneUnique",
                table: "UserAccounts",
                type: "varchar(255)",
                nullable: true,
                computedColumnSql: "NULLIF(`Phone`, '')",
                stored: false);

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_EmailUnique",
                table: "UserAccounts",
                column: "EmailUnique",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_PhoneUnique",
                table: "UserAccounts",
                column: "PhoneUnique",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_EmailUnique",
                table: "UserAccounts");

            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_PhoneUnique",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "EmailUnique",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PhoneUnique",
                table: "UserAccounts");
        }
    }
}
