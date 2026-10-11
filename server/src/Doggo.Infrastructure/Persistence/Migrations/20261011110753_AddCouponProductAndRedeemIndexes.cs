using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doggo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCouponProductAndRedeemIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SignRecords_UserId_CycleId",
                table: "SignRecords");

            migrationBuilder.CreateTable(
                name: "CouponProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CouponId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ProductId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponProducts", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_UserRedeemRecords_RedeemCodeId_UserId",
                table: "UserRedeemRecords",
                columns: new[] { "RedeemCodeId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignRecords_UserId_CycleId",
                table: "SignRecords",
                columns: new[] { "UserId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_SignRecords_UserId_SignedDate",
                table: "SignRecords",
                columns: new[] { "UserId", "SignedDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CouponProducts_CouponId_ProductId",
                table: "CouponProducts",
                columns: new[] { "CouponId", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CouponProducts");

            migrationBuilder.DropIndex(
                name: "IX_UserRedeemRecords_RedeemCodeId_UserId",
                table: "UserRedeemRecords");

            migrationBuilder.DropIndex(
                name: "IX_SignRecords_UserId_CycleId",
                table: "SignRecords");

            migrationBuilder.DropIndex(
                name: "IX_SignRecords_UserId_SignedDate",
                table: "SignRecords");

            migrationBuilder.CreateIndex(
                name: "IX_SignRecords_UserId_CycleId",
                table: "SignRecords",
                columns: new[] { "UserId", "CycleId" },
                unique: true);
        }
    }
}
