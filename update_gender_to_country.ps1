# PowerShell script to update SQL Server database schema - Change Gender to Country

Write-Host "Updating SQL Server database schema - Changing Gender to Country..." -ForegroundColor Green

# Execute SQL command in SQL Server container
$sql = @"
USE IqTestDb;

-- First, drop the Gender column
ALTER TABLE Users DROP COLUMN Gender;

-- Add the Country column
ALTER TABLE Users ADD Country NVARCHAR(100) NULL;

-- Make Age nullable
ALTER TABLE Users ALTER COLUMN Age INT NULL;

PRINT 'Database schema updated successfully - Gender changed to Country.';
"@

# Execute directly with docker exec
docker exec -i iqtest-db-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'YourStrong@Passw0rd' -C -Q $sql

Write-Host "Database update completed!" -ForegroundColor Green