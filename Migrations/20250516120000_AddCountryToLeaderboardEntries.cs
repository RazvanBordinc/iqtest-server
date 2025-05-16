using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IqTest_server.Migrations
{
    /// <inheritdoc />
    public partial class AddCountryToLeaderboardEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "LeaderboardEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "United States");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Country",
                table: "LeaderboardEntries");
        }
    }
}