using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doggo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BannerScheduleWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EndAtUtc",
                table: "Banners",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartAtUtc",
                table: "Banners",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndAtUtc",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "StartAtUtc",
                table: "Banners");
        }
    }
}
