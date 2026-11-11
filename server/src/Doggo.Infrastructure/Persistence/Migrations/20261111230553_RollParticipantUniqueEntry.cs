using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doggo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RollParticipantUniqueEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Normalize legacy null references first so the NOT NULL conversion and the
            // unique index keep every existing row.
            migrationBuilder.Sql(
                "UPDATE `RollRoomParticipants` SET `EntryRef` = '' WHERE `EntryRef` IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "EntryRef",
                table: "RollRoomParticipants",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RollRoomParticipants_RoomId_UserId_EntryRef",
                table: "RollRoomParticipants",
                columns: new[] { "RoomId", "UserId", "EntryRef" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RollRoomParticipants_RoomId_UserId_EntryRef",
                table: "RollRoomParticipants");

            migrationBuilder.AlterColumn<string>(
                name: "EntryRef",
                table: "RollRoomParticipants",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128);
        }
    }
}
