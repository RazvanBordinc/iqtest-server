using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IqTest_server.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add indexes for LeaderboardEntries table
            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardEntries_TestTypeId_Score_DESC",
                table: "LeaderboardEntries",
                columns: new[] { "TestTypeId", "Score" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardEntries_Score_DESC",
                table: "LeaderboardEntries",
                column: "Score",
                descending: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardEntries_LastUpdated",
                table: "LeaderboardEntries",
                column: "LastUpdated");

            // Add indexes for TestResults table
            migrationBuilder.CreateIndex(
                name: "IX_TestResults_UserId_TestTypeId_CompletedAt",
                table: "TestResults",
                columns: new[] { "UserId", "TestTypeId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TestResults_Score_DESC",
                table: "TestResults",
                column: "Score",
                descending: true);

            // Add index for Users table
            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username");

            // Add index for Questions table
            migrationBuilder.CreateIndex(
                name: "IX_Questions_TestTypeId",
                table: "Questions",
                column: "TestTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeaderboardEntries_TestTypeId_Score_DESC",
                table: "LeaderboardEntries");

            migrationBuilder.DropIndex(
                name: "IX_LeaderboardEntries_Score_DESC",
                table: "LeaderboardEntries");

            migrationBuilder.DropIndex(
                name: "IX_LeaderboardEntries_LastUpdated",
                table: "LeaderboardEntries");

            migrationBuilder.DropIndex(
                name: "IX_TestResults_UserId_TestTypeId_CompletedAt",
                table: "TestResults");

            migrationBuilder.DropIndex(
                name: "IX_TestResults_Score_DESC",
                table: "TestResults");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Questions_TestTypeId",
                table: "Questions");
        }
    }
}