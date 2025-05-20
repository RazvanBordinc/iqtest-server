using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IqTest_server.Migrations
{
    public partial class EnsureCountryColumnExists : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Check if column exists and add if not
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 
                    FROM sys.columns 
                    WHERE object_id = OBJECT_ID(N'[dbo].[LeaderboardEntries]') 
                    AND name = 'Country'
                )
                BEGIN
                    ALTER TABLE [dbo].[LeaderboardEntries] 
                    ADD [Country] nvarchar(100) NOT NULL DEFAULT N'United States';
                END
                ELSE
                BEGIN
                    -- Ensure the column properties match the model
                    DECLARE @nullable bit;
                    DECLARE @maxLength int;
                    
                    SELECT @nullable = is_nullable, @maxLength = max_length
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[dbo].[LeaderboardEntries]') 
                    AND name = 'Country';
                    
                    -- If the column is nullable but should not be, add default and make non-nullable
                    IF @nullable = 1
                    BEGIN
                        -- Set default value for NULL columns
                        UPDATE [dbo].[LeaderboardEntries] 
                        SET [Country] = N'United States'
                        WHERE [Country] IS NULL;
                        
                        -- Make column NOT NULL
                        ALTER TABLE [dbo].[LeaderboardEntries]
                        ALTER COLUMN [Country] nvarchar(100) NOT NULL;
                    END
                    
                    -- If the column length doesn't match, update it
                    IF @maxLength != 100
                    BEGIN
                        ALTER TABLE [dbo].[LeaderboardEntries]
                        ALTER COLUMN [Country] nvarchar(100) NOT NULL;
                    END
                END
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // We don't drop the column on down migration to avoid data loss
            // Instead, we just make it nullable
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 
                    FROM sys.columns 
                    WHERE object_id = OBJECT_ID(N'[dbo].[LeaderboardEntries]') 
                    AND name = 'Country'
                )
                BEGIN
                    ALTER TABLE [dbo].[LeaderboardEntries]
                    ALTER COLUMN [Country] nvarchar(100) NULL;
                END
            ");
        }
    }
}