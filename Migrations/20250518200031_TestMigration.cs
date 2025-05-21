using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IqTest_server.Migrations
{
    /// <inheritdoc />
    public partial class TestMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Duration",
                table: "TestResults",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Answers",
                type: "int",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "TestTypes",
                keyColumn: "Id",
                keyValue: 1,
                column: "TimeLimit",
                value: "18 minutes");

            migrationBuilder.UpdateData(
                table: "TestTypes",
                keyColumn: "Id",
                keyValue: 2,
                column: "TimeLimit",
                value: "20 minutes");

            migrationBuilder.UpdateData(
                table: "TestTypes",
                keyColumn: "Id",
                keyValue: 3,
                column: "TimeLimit",
                value: "15 minutes");

            migrationBuilder.UpdateData(
                table: "TestTypes",
                keyColumn: "Id",
                keyValue: 4,
                column: "TimeLimit",
                value: "35 minutes");

            migrationBuilder.CreateIndex(
                name: "IX_Answers_UserId",
                table: "Answers",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Answers_Users_UserId",
                table: "Answers",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Answers_Users_UserId",
                table: "Answers");

            migrationBuilder.DropIndex(
                name: "IX_Answers_UserId",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Answers");

            migrationBuilder.AlterColumn<string>(
                name: "Duration",
                table: "TestResults",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "TestTypes",
                keyColumn: "Id",
                keyValue: 1,
                column: "TimeLimit",
                value: "25 minutes");

            migrationBuilder.UpdateData(
                table: "TestTypes",
                keyColumn: "Id",
                keyValue: 2,
                column: "TimeLimit",
                value: "30 minutes");

            migrationBuilder.UpdateData(
                table: "TestTypes",
                keyColumn: "Id",
                keyValue: 3,
                column: "TimeLimit",
                value: "22 minutes");

            migrationBuilder.UpdateData(
                table: "TestTypes",
                keyColumn: "Id",
                keyValue: 4,
                column: "TimeLimit",
                value: "45 minutes");
        }
    }
}
