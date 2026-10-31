USE TEMPINTERFACE
GO

IF OBJECT_ID(N'dbo.L_ORDR', N'U') IS NULL
    THROW 51000, 'L_ORDR does not exist. Use Create_TEMPINTERFACE.sql for a new database.', 1;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.L_ORDR', N'Log_ID') IS NULL
    ALTER TABLE dbo.L_ORDR ADD Log_ID INT IDENTITY(1,1) NOT NULL;

IF COL_LENGTH(N'dbo.L_ORDR', N'DocEntry') IS NULL
    ALTER TABLE dbo.L_ORDR ADD DocEntry INT NULL;

IF COL_LENGTH(N'dbo.L_ORDR', N'U_Web_NO') IS NOT NULL
    ALTER TABLE dbo.L_ORDR DROP COLUMN U_Web_NO;

-- The original Order_ID column, when present, keeps its historical values.
-- New requests use Log_ID to identify each log row.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.L_ORDR') AND name = N'UX_L_ORDR_Log_ID')
    EXEC(N'CREATE UNIQUE INDEX UX_L_ORDR_Log_ID ON dbo.L_ORDR(Log_ID)');

COMMIT TRANSACTION;
GO
