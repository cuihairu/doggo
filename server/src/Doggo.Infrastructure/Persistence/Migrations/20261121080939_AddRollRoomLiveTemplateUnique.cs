using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doggo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRollRoomLiveTemplateUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LiveTemplateId",
                table: "RollRooms",
                type: "char(36)",
                nullable: true,
                computedColumnSql: "CASE WHEN `Status` <> 5 AND `TemplateId` IS NOT NULL THEN `TemplateId` ELSE NULL END",
                stored: false);

            migrationBuilder.CreateIndex(
                name: "IX_RollRooms_LiveTemplateId",
                table: "RollRooms",
                column: "LiveTemplateId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RollRooms_LiveTemplateId",
                table: "RollRooms");

            migrationBuilder.DropColumn(
                name: "LiveTemplateId",
                table: "RollRooms");
        }
    }
}
