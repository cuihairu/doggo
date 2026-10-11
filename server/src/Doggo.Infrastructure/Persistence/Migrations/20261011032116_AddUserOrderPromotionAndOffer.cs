using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doggo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserOrderPromotionAndOffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PromotionId",
                table: "UserOrders",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeOfferId",
                table: "UserOrders",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PromotionId",
                table: "UserOrders");

            migrationBuilder.DropColumn(
                name: "TradeOfferId",
                table: "UserOrders");
        }
    }
}
