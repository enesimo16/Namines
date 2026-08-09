CREATE TABLE "Users" (
    "Id" SERIAL NOT NULL,
    "Email" NVARCHAR(255) NOT NULL,
    "CreatedAt" DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    , CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
);

CREATE TABLE "Products" (
    "Id" SERIAL NOT NULL,
    "Name" NVARCHAR(200) NOT NULL,
    "Price" DECIMAL NOT NULL,
    "Stock" INT NOT NULL DEFAULT 0
    , CONSTRAINT "PK_Products" PRIMARY KEY ("Id")
);

CREATE TABLE "Orders" (
    "Id" SERIAL NOT NULL,
    "UserId" INT NOT NULL,
    "Total" DECIMAL NOT NULL,
    "PlacedAt" DATETIME2 NOT NULL
    , CONSTRAINT "PK_Orders" PRIMARY KEY ("Id")
);

CREATE TABLE "OrderItems" (
    "Id" SERIAL NOT NULL,
    "OrderId" INT NOT NULL,
    "ProductId" INT NOT NULL,
    "Quantity" INT NOT NULL DEFAULT 1
    , CONSTRAINT "PK_OrderItems" PRIMARY KEY ("Id")
);

ALTER TABLE "Orders" ADD CONSTRAINT "FK_Orders_Users_UserId" FOREIGN KEY("UserId")
REFERENCES "Users" ("Id");

ALTER TABLE "OrderItems" ADD CONSTRAINT "FK_OrderItems_Orders_OrderId" FOREIGN KEY("OrderId")
REFERENCES "Orders" ("Id");

ALTER TABLE "OrderItems" ADD CONSTRAINT "FK_OrderItems_Products_ProductId" FOREIGN KEY("ProductId")
REFERENCES "Products" ("Id");

