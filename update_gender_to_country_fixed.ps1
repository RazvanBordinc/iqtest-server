# PowerShell script to update SQL Server database schema - Change Gender to Country

Write-Host "Updating SQL Server database schema - Changing Gender to Country..." -ForegroundColor Green

# Execute SQL command in SQL Server container
$sql = @"
USE IqTestDb;

-- First, find and drop any default constraints on the Gender column
DECLARE @constraintName NVARCHAR(256);
SELECT @constraintName = name 
FROM sys.default_constraints 
WHERE parent_object_id = OBJECT_ID('Users') 
AND parent_column_id = (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'Gender');

IF @constraintName IS NOT NULL
BEGIN
    EXEC('ALTER TABLE Users DROP CONSTRAINT ' + @constraintName);
END

-- Now drop the Gender column
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'Gender')
BEGIN
    ALTER TABLE Users DROP COLUMN Gender;
END

-- Add the Country column if it doesn't exist
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'Country')
BEGIN
    ALTER TABLE Users ADD Country NVARCHAR(100) NULL;
END

-- Make Age nullable if it isn't already
ALTER TABLE Users ALTER COLUMN Age INT NULL;

PRINT 'Database schema updated successfully - Gender changed to Country.';
"@

# Execute directly with docker exec
docker exec -i iqtest-db-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'YourStrong@Passw0rd' -C -Q $sql

Write-Host "Database update completed!" -ForegroundColor Green