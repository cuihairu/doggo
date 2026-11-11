using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doggo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionPlanVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "RollRoomParticipants",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "PromotionPlans",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionPlans_ProductId",
                table: "PromotionPlans",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PromotionPlans_ProductId",
                table: "PromotionPlans");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "RollRoomParticipants");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "PromotionPlans");
        }
    }
}
