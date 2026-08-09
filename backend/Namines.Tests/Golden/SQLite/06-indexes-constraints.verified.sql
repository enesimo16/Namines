PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS "Users" (
    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    "Email" TEXT NOT NULL,
    "CountryCode" TEXT NOT NULL DEFAULT 'TR',
    "Age" INTEGER NULL,
    "CreatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "UQ_Users_Email" UNIQUE ("Email"),
    CONSTRAINT "CK_Users_Age" CHECK (Age IS NULL OR Age >= 0)
);

CREATE INDEX "IX_Users_CountryCode_CreatedAt" ON "Users" ("CountryCode", "CreatedAt" DESC);
CREATE UNIQUE INDEX "UX_Users_Email_Active" ON "Users" ("Email") WHERE DeletedAt IS NULL;
CREATE INDEX "IX_Users_CreatedAt" ON "Users" ("CreatedAt") /* INCLUDE (Email) — SQLite desteklemiyor */;

CREATE TABLE IF NOT EXISTS "Orders" (
    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Total" REAL NOT NULL,
    FOREIGN KEY ("UserId") REFERENCES "Users" ("Id"),
    CONSTRAINT "CK_Orders_ck2" CHECK (Total >= 0)
);

CREATE INDEX "IX_Orders_UserId" ON "Orders" ("UserId");

