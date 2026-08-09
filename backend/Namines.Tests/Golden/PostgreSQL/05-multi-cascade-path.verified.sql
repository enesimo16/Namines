CREATE TABLE "Users" (
    "Id" SERIAL NOT NULL,
    "Email" NVARCHAR(255) NOT NULL
    , CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
);

CREATE TABLE "Addresses" (
    "Id" SERIAL NOT NULL,
    "UserId" INT NOT NULL,
    "Line1" NVARCHAR(200) NOT NULL
    , CONSTRAINT "PK_Addresses" PRIMARY KEY ("Id")
);

CREATE TABLE "Orders" (
    "Id" SERIAL NOT NULL,
    "UserId" INT NOT NULL,
    "AddressId" INT NOT NULL
    , CONSTRAINT "PK_Orders" PRIMARY KEY ("Id")
);

ALTER TABLE "Addresses" ADD CONSTRAINT "FK_Addresses_Users_UserId" FOREIGN KEY("UserId")
REFERENCES "Users" ("Id")
ON DELETE CASCADE;

ALTER TABLE "Orders" ADD CONSTRAINT "FK_Orders_Users_UserId" FOREIGN KEY("UserId")
REFERENCES "Users" ("Id")
ON DELETE CASCADE;

ALTER TABLE "Orders" ADD CONSTRAINT "FK_Orders_Addresses_AddressId" FOREIGN KEY("AddressId")
REFERENCES "Addresses" ("Id")
ON DELETE CASCADE;

