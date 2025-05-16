# PowerShell script to update SQL Server database schema

Write-Host "Updating SQL Server database schema..." -ForegroundColor Green

# Execute SQL command in SQL Server container
$sql = @"
USE IqTestDb;

-- Check if columns already exist before adding
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'LeaderboardEntries' AND COLUMN_NAME = 'AverageTime')
BEGIN
    ALTER TABLE LeaderboardEntries ADD AverageTime NVARCHAR(MAX) NULL DEFAULT '';
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'LeaderboardEntries' AND COLUMN_NAME = 'BestTime')
BEGIN
    ALTER TABLE LeaderboardEntries ADD BestTime NVARCHAR(MAX) NULL DEFAULT '';
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'LeaderboardEntries' AND COLUMN_NAME = 'IQScore')
BEGIN
    ALTER TABLE LeaderboardEntries ADD IQScore INT NULL;
END

-- Update TestResult table if needed
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TestResults' AND COLUMN_NAME = 'TimeTaken')
BEGIN
    ALTER TABLE TestResults ADD TimeTaken NVARCHAR(MAX) NULL DEFAULT '';
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TestResults' AND COLUMN_NAME = 'IQScore')
BEGIN
    ALTER TABLE TestResults ADD IQScore INT NULL;
END

-- Update existing NULL values to empty strings
UPDATE LeaderboardEntries SET AverageTime = '' WHERE AverageTime IS NULL;
UPDATE LeaderboardEntries SET BestTime = '' WHERE BestTime IS NULL;
UPDATE TestResults SET TimeTaken = '' WHERE TimeTaken IS NULL;

PRINT 'Database schema updated successfully.';
"@

# Execute directly with docker exec
docker exec -i iqtest-db-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'YourStrong@Passw0rd' -C -Q $sql

Write-Host "Database update completed!" -ForegroundColor Green