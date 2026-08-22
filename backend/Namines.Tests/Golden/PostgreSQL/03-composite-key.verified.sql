CREATE TABLE "Orders" (
    "Id" SERIAL NOT NULL
    , CONSTRAINT "PK_Orders" PRIMARY KEY ("Id")
);

CREATE TABLE "Products" (
    "Id" SERIAL NOT NULL
    , CONSTRAINT "PK_Products" PRIMARY KEY ("Id")
);

CREATE TABLE "OrderProducts" (
    "OrderId" integer NOT NULL,
    "ProductId" integer NOT NULL,
    "Quantity" integer NOT NULL
    , CONSTRAINT "PK_OrderProducts" PRIMARY KEY ("OrderId", "ProductId")
);

ALTER TABLE "OrderProducts" ADD CONSTRAINT "FK_OrderProducts_Orders_OrderId" FOREIGN KEY("OrderId")
REFERENCES "Orders" ("Id");

ALTER TABLE "OrderProducts" ADD CONSTRAINT "FK_OrderProducts_Products_ProductId" FOREIGN KEY("ProductId")
REFERENCES "Products" ("Id");

