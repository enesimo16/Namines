CREATE TABLE "Users" (
    "Id" SERIAL NOT NULL,
    "Email" varchar(255) NOT NULL,
    "CountryCode" char(2) NOT NULL DEFAULT 'TR',
    "Age" integer NULL,
    "CreatedAt" timestamp NOT NULL,
    "DeletedAt" timestamp NULL
    , CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
    , CONSTRAINT "UQ_Users_Email" UNIQUE ("Email")
    , CONSTRAINT "CK_Users_Age" CHECK ("Age" IS NULL OR "Age" >= 0)
);

CREATE INDEX "IX_Users_CountryCode_CreatedAt" ON "Users" ("CountryCode", "CreatedAt" DESC);
CREATE UNIQUE INDEX "UX_Users_Email_Active" ON "Users" ("Email") WHERE "DeletedAt" IS NULL;
CREATE INDEX "IX_Users_CreatedAt" ON "Users" ("CreatedAt") INCLUDE ("Email");

CREATE TABLE "Orders" (
    "Id" SERIAL NOT NULL,
    "UserId" integer NOT NULL,
    "Total" numeric NOT NULL
    , CONSTRAINT "PK_Orders" PRIMARY KEY ("Id")
    , CONSTRAINT "CK_Orders_ck2" CHECK ("Total" >= 0)
);

CREATE INDEX "IX_Orders_UserId" ON "Orders" ("UserId");

ALTER TABLE "Orders" ADD CONSTRAINT "FK_Orders_Users_UserId" FOREIGN KEY("UserId")
REFERENCES "Users" ("Id");

