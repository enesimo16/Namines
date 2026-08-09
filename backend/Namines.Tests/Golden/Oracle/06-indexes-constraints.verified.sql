CREATE TABLE "Users" (
    "Id" NUMBER(10) GENERATED ALWAYS AS IDENTITY NOT NULL,
    "Email" NVARCHAR2(255) NOT NULL,
    "CountryCode" CHAR(2) NOT NULL DEFAULT 'TR',
    "Age" NUMBER(10) NULL,
    "CreatedAt" NVARCHAR2(255) NOT NULL,
    "DeletedAt" NVARCHAR2(255) NULL,
    CONSTRAINT "PK_Users" PRIMARY KEY ("Id"),
    CONSTRAINT "UQ_Users_Email" UNIQUE ("Email"),
    CONSTRAINT "CK_Users_Age" CHECK ("Age" IS NULL OR "Age" >= 0)
);

CREATE INDEX "IX_Users_CountryCode_CreatedAt" ON "Users" ("CountryCode", "CreatedAt" DESC);
CREATE UNIQUE INDEX "UX_Users_Email_Active" ON "Users" ("Email") /* WHERE "DeletedAt" IS NULL — Oracle kısmi index desteklemiyor */;
CREATE INDEX "IX_Users_CreatedAt" ON "Users" ("CreatedAt") /* INCLUDE (Email) — Oracle desteklemiyor */;

CREATE TABLE "Orders" (
    "Id" NUMBER(10) GENERATED ALWAYS AS IDENTITY NOT NULL,
    "UserId" NUMBER(10) NOT NULL,
    "Total" NUMBER(18,4) NOT NULL,
    CONSTRAINT "PK_Orders" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Orders_ck2" CHECK ("Total" >= 0)
);

CREATE INDEX "IX_Orders_UserId" ON "Orders" ("UserId");

ALTER TABLE "Orders"
    ADD CONSTRAINT "FK_Orders_Users_UserId"
    FOREIGN KEY ("UserId")
    REFERENCES "Users" ("Id");

