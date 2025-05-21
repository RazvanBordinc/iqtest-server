using Microsoft.EntityFrameworkCore.Migrations;

namespace IqTest_server.Migrations
{
    public partial class AddTimeAndIQFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add columns to LeaderboardEntries table
            migrationBuilder.AddColumn<string>(
                name: "AverageTime",
                table: "LeaderboardEntries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BestTime",
                table: "LeaderboardEntries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IQScore",
                table: "LeaderboardEntries",
                type: "int",
                nullable: true);

            // Add IQScore column to TestResults table
            migrationBuilder.AddColumn<int>(
                name: "IQScore",
                table: "TestResults",
                type: "int",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove columns from LeaderboardEntries table
            migrationBuilder.DropColumn(
                name: "AverageTime",
                table: "LeaderboardEntries");

            migrationBuilder.DropColumn(
                name: "BestTime",
                table: "LeaderboardEntries");

            migrationBuilder.DropColumn(
                name: "IQScore",
                table: "LeaderboardEntries");

            // Remove IQScore column from TestResults table
            migrationBuilder.DropColumn(
                name: "IQScore",
                table: "TestResults");
        }
    }
}