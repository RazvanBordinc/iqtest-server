#!/bin/bash

# Bash script to update SQL Server database schema

echo -e "\033[32mUpdating SQL Server database schema...\033[0m"

# SQL commands to add new columns
sql="
USE IqTestDb;

-- Check if columns already exist before adding
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'LeaderboardEntries' AND COLUMN_NAME = 'AverageTime')
BEGIN
    ALTER TABLE LeaderboardEntries ADD AverageTime NVARCHAR(MAX) NULL;
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'LeaderboardEntries' AND COLUMN_NAME = 'BestTime')
BEGIN
    ALTER TABLE LeaderboardEntries ADD BestTime NVARCHAR(MAX) NULL;
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'LeaderboardEntries' AND COLUMN_NAME = 'IQScore')
BEGIN
    ALTER TABLE LeaderboardEntries ADD IQScore INT NULL;
END

-- Update TestResult table if needed
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TestResults' AND COLUMN_NAME = 'TimeTaken')
BEGIN
    ALTER TABLE TestResults ADD TimeTaken NVARCHAR(MAX) NULL;
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TestResults' AND COLUMN_NAME = 'IQScore')
BEGIN
    ALTER TABLE TestResults ADD IQScore INT NULL;
END

PRINT 'Database schema updated successfully.';
"

# Execute SQL command in SQL Server container
docker exec -i iqtest-db-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'YourStrong!Passw0rd' -C -Q "$sql"

echo -e "\033[32mDatabase update completed!\033[0m"