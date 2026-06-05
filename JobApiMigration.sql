CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "android_auth_tokens" (
    "TokenId" TEXT NOT NULL CONSTRAINT "PK_android_auth_tokens" PRIMARY KEY,
    "DeviceName" TEXT NOT NULL,
    "AppInstanceId" TEXT NOT NULL,
    "TokenHash" TEXT NOT NULL,
    "Scopes" TEXT NOT NULL,
    "AuthMethod" TEXT NOT NULL,
    "ClientIp" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "ExpiresAt" TEXT NOT NULL,
    "LastUsedAt" TEXT NOT NULL,
    "LastExtendedAt" TEXT NOT NULL,
    "RefreshEligibleAt" TEXT NOT NULL
);

CREATE TABLE "android_pairing_requests" (
    "PairingId" TEXT NOT NULL CONSTRAINT "PK_android_pairing_requests" PRIMARY KEY,
    "DeviceName" TEXT NOT NULL,
    "AppInstanceId" TEXT NOT NULL,
    "CodeHash" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "ExpiresAt" TEXT NOT NULL,
    "AttemptCount" INTEGER NOT NULL,
    "Status" TEXT NOT NULL
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260605194939_AddAndroidAuthSchemas', '9.0.0');

CREATE TABLE "job_states" (
    "JobId" TEXT NOT NULL CONSTRAINT "PK_job_states" PRIMARY KEY,
    "Sequence" INTEGER NOT NULL,
    "Progress" REAL NOT NULL,
    "Phase" TEXT NOT NULL,
    "LatestMessage" TEXT NOT NULL,
    "PendingApproval" INTEGER NOT NULL,
    "ChangedFiles" TEXT NOT NULL,
    "VerificationState" TEXT NOT NULL
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260605202059_AddJobStates', '9.0.0');

COMMIT;

