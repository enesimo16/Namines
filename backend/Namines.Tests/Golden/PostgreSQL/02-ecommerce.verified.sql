CREATE TABLE "Users" (
    "Id" SERIAL NOT NULL,
    "Email" varchar(255) NOT NULL,
    "CreatedAt" timestamp NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
    , CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
);

CREATE TABLE "Products" (
    "Id" SERIAL NOT NULL,
    "Name" varchar(200) NOT NULL,
    "Price" numeric NOT NULL,
    "Stock" integer NOT NULL DEFAULT 0
    , CONSTRAINT "PK_Products" PRIMARY KEY ("Id")
);

CREATE TABLE "Orders" (
    "Id" SERIAL NOT NULL,
    "UserId" integer NOT NULL,
    "Total" numeric NOT NULL,
    "PlacedAt" timestamp NOT NULL
    , CONSTRAINT "PK_Orders" PRIMARY KEY ("Id")
);

CREATE TABLE "OrderItems" (
    "Id" SERIAL NOT NULL,
    "OrderId" integer NOT NULL,
    "ProductId" integer NOT NULL,
    "Quantity" integer NOT NULL DEFAULT 1
    , CONSTRAINT "PK_OrderItems" PRIMARY KEY ("Id")
);

ALTER TABLE "Orders" ADD CONSTRAINT "FK_Orders_Users_UserId" FOREIGN KEY("UserId")
REFERENCES "Users" ("Id");

ALTER TABLE "OrderItems" ADD CONSTRAINT "FK_OrderItems_Orders_OrderId" FOREIGN KEY("OrderId")
REFERENCES "Orders" ("Id");

ALTER TABLE "OrderItems" ADD CONSTRAINT "FK_OrderItems_Products_ProductId" FOREIGN KEY("ProductId")
REFERENCES "Products" ("Id");

