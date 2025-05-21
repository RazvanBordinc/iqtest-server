using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IqTest_server.Migrations
{
    /// <inheritdoc />
    public partial class AddCountryToLeaderboardEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add Country column if it doesn't exist
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 
                    FROM sys.columns 
                    WHERE object_id = OBJECT_ID(N'[dbo].[LeaderboardEntries]') 
                    AND name = 'Country'
                )
                BEGIN
                    ALTER TABLE [dbo].[LeaderboardEntries] 
                    ADD [Country] nvarchar(100) NOT NULL DEFAULT N'United States'
                END
            ");
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