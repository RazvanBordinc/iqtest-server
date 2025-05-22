using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IqTest_server.Migrations
{
    /// <inheritdoc />
    public partial class TestMigration2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // First check if the Country column exists, if not create it
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 
                    FROM sys.columns 
                    WHERE object_id = OBJECT_ID(N'[dbo].[LeaderboardEntries]') 
                    AND name = 'Country'
                )
                BEGIN
                    ALTER TABLE [dbo].[LeaderboardEntries] 
                    ADD [Country] nvarchar(100) NULL;
                END
            ");

            // Now update NULL values to empty string and make column NOT NULL
            migrationBuilder.Sql(@"
                UPDATE [LeaderboardEntries] SET [Country] = N'' WHERE [Country] IS NULL;
            ");

            migrationBuilder.AlterColumn<string>(
                name: "Country",
                table: "LeaderboardEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Check if column exists before trying to alter it
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 
                    FROM sys.columns 
                    WHERE object_id = OBJECT_ID(N'[dbo].[LeaderboardEntries]') 
                    AND name = 'Country'
                )
                BEGIN
                    ALTER TABLE [LeaderboardEntries] ALTER COLUMN [Country] nvarchar(100) NULL;
                END
            ");
        }
    }
}
