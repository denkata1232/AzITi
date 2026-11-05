-- One-time, data-preserving baseline for the legacy NiePostigameDB schema.
-- Back up the database before running. Run this script once against NiePostigameDB;
-- it reconciles legacy column names and records the migrations already represented by this schema.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'NiePostigameDB'
	THROW 51000, 'This baseline can only run against NiePostigameDB.', 1;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
	THROW 51000, 'EF migrations history table is missing; inspect the database before baselining.', 1;

IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory)
	THROW 51000, 'EF migration history is not empty; this baseline is for an untracked legacy database only.', 1;

IF OBJECT_ID(N'dbo.AidCampaigns', N'U') IS NULL
   OR OBJECT_ID(N'dbo.CampaignForms', N'U') IS NULL
   OR OBJECT_ID(N'dbo.UserDocuments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.UserGmailProfiles', N'U') IS NULL
   OR OBJECT_ID(N'dbo.UserProfiles', N'U') IS NULL
	THROW 51000, 'Expected legacy tables are missing; inspect the database before baselining.', 1;

BEGIN TRY
	BEGIN TRANSACTION;

	IF COL_LENGTH(N'dbo.CampaignForms', N'Name') IS NOT NULL
	   AND COL_LENGTH(N'dbo.CampaignForms', N'DocumentName') IS NOT NULL
		THROW 51000, 'Both CampaignForms.Name and DocumentName exist; resolve their data before baselining.', 1;

	IF COL_LENGTH(N'dbo.CampaignForms', N'Name') IS NULL
	   AND COL_LENGTH(N'dbo.CampaignForms', N'DocumentName') IS NOT NULL
		EXEC sys.sp_rename N'dbo.CampaignForms.DocumentName', N'Name', N'COLUMN';

	IF COL_LENGTH(N'dbo.CampaignForms', N'FormUrl') IS NOT NULL
	   AND COL_LENGTH(N'dbo.CampaignForms', N'FilePath') IS NOT NULL
		THROW 51000, 'Both CampaignForms.FormUrl and FilePath exist; resolve their data before baselining.', 1;

	IF COL_LENGTH(N'dbo.CampaignForms', N'FormUrl') IS NULL
	   AND COL_LENGTH(N'dbo.CampaignForms', N'FilePath') IS NOT NULL
		EXEC sys.sp_rename N'dbo.CampaignForms.FilePath', N'FormUrl', N'COLUMN';

	IF COL_LENGTH(N'dbo.CampaignForms', N'Name') IS NULL
	   OR COL_LENGTH(N'dbo.CampaignForms', N'FormUrl') IS NULL
		THROW 51000, 'CampaignForms does not contain recognizable legacy or current columns.', 1;

	IF COL_LENGTH(N'dbo.AidCampaigns', N'LogoUrl') IS NULL
		ALTER TABLE dbo.AidCampaigns ADD LogoUrl nvarchar(max) NULL;
	IF COL_LENGTH(N'dbo.AidCampaigns', N'AidType') IS NULL
		ALTER TABLE dbo.AidCampaigns ADD AidType nvarchar(max) NULL;
	IF COL_LENGTH(N'dbo.AidCampaigns', N'SimplifiedRequirement') IS NULL
		ALTER TABLE dbo.AidCampaigns ADD SimplifiedRequirement nvarchar(max) NULL;
	IF COL_LENGTH(N'dbo.AidCampaigns', N'ContactEmail') IS NULL
		ALTER TABLE dbo.AidCampaigns ADD ContactEmail nvarchar(max) NULL;
	IF COL_LENGTH(N'dbo.AidCampaigns', N'GoogleFormUrl') IS NULL
		ALTER TABLE dbo.AidCampaigns ADD GoogleFormUrl nvarchar(max) NULL;

	ALTER TABLE dbo.AidCampaigns ALTER COLUMN Title nvarchar(max) NOT NULL;
	ALTER TABLE dbo.AidCampaigns ALTER COLUMN Description nvarchar(max) NULL;
	ALTER TABLE dbo.AidCampaigns ALTER COLUMN LogoUrl nvarchar(max) NULL;
	ALTER TABLE dbo.AidCampaigns ALTER COLUMN AidType nvarchar(max) NULL;
	ALTER TABLE dbo.AidCampaigns ALTER COLUMN SimplifiedRequirement nvarchar(max) NULL;
	ALTER TABLE dbo.AidCampaigns ALTER COLUMN ContactEmail nvarchar(max) NULL;
	ALTER TABLE dbo.AidCampaigns ALTER COLUMN GoogleFormUrl nvarchar(max) NULL;

	ALTER TABLE dbo.CampaignForms ALTER COLUMN Name nvarchar(max) NOT NULL;
	ALTER TABLE dbo.CampaignForms ALTER COLUMN FormUrl nvarchar(max) NOT NULL;

	ALTER TABLE dbo.UserGmailProfiles ALTER COLUMN UserId nvarchar(max) NOT NULL;
	ALTER TABLE dbo.UserGmailProfiles ALTER COLUMN GmailAddress nvarchar(max) NOT NULL;
	ALTER TABLE dbo.UserGmailProfiles ALTER COLUMN AccessToken nvarchar(max) NULL;
	ALTER TABLE dbo.UserGmailProfiles ALTER COLUMN RefreshToken nvarchar(max) NULL;

	ALTER TABLE dbo.UserProfiles ALTER COLUMN UserId nvarchar(max) NOT NULL;
	ALTER TABLE dbo.UserProfiles ALTER COLUMN FirstName nvarchar(max) NULL;
	ALTER TABLE dbo.UserProfiles ALTER COLUMN MiddleName nvarchar(max) NULL;
	ALTER TABLE dbo.UserProfiles ALTER COLUMN LastName nvarchar(max) NULL;

	ALTER TABLE dbo.UserDocuments ALTER COLUMN DocumentType nvarchar(max) NOT NULL;
	ALTER TABLE dbo.UserDocuments ALTER COLUMN FilePath nvarchar(max) NULL;
	ALTER TABLE dbo.UserDocuments ALTER COLUMN OriginalFileName nvarchar(max) NULL;

	IF OBJECT_ID(N'dbo.UserApplications', N'U') IS NULL
	BEGIN
		CREATE TABLE dbo.UserApplications
		(
			Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserApplications PRIMARY KEY,
			UserId nvarchar(max) NOT NULL,
			CampaignId int NOT NULL,
			AppliedAt datetime2 NOT NULL,
			Status nvarchar(max) NOT NULL,
			CONSTRAINT FK_UserApplications_AidCampaigns_CampaignId
				FOREIGN KEY (CampaignId) REFERENCES dbo.AidCampaigns(Id) ON DELETE CASCADE
		);
	END;

	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CampaignForms') AND name = N'IX_CampaignForms_AidCampaignId')
		CREATE INDEX IX_CampaignForms_AidCampaignId ON dbo.CampaignForms(AidCampaignId);

	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.UserApplications') AND name = N'IX_UserApplications_CampaignId')
		CREATE INDEX IX_UserApplications_CampaignId ON dbo.UserApplications(CampaignId);

	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.UserDocuments') AND name = N'IX_UserDocuments_UserProfileId')
		CREATE INDEX IX_UserDocuments_UserProfileId ON dbo.UserDocuments(UserProfileId);

	INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
	VALUES
		(N'20261011125345_InitialCreate', N'10.0.0'),
		(N'20261104192144_idfk', N'10.0.0');

	COMMIT TRANSACTION;
END TRY
BEGIN CATCH
	IF XACT_STATE() <> 0
		ROLLBACK TRANSACTION;
	THROW;
END CATCH;
