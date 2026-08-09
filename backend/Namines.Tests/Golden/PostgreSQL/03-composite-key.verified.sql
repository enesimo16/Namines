CREATE TABLE "Orders" (
    "Id" SERIAL NOT NULL
    , CONSTRAINT "PK_Orders" PRIMARY KEY ("Id")
);

CREATE TABLE "Products" (
    "Id" SERIAL NOT NULL
    , CONSTRAINT "PK_Products" PRIMARY KEY ("Id")
);

CREATE TABLE "OrderProducts" (
    "OrderId" SERIAL NOT NULL,
    "ProductId" SERIAL NOT NULL,
    "Quantity" INT NOT NULL
    , CONSTRAINT "PK_OrderProducts" PRIMARY KEY ("OrderId", "ProductId")
);

ALTER TABLE "OrderProducts" ADD CONSTRAINT "FK_OrderProducts_Orders_OrderId" FOREIGN KEY("OrderId")
REFERENCES "Orders" ("Id");

ALTER TABLE "OrderProducts" ADD CONSTRAINT "FK_OrderProducts_Products_ProductId" FOREIGN KEY("ProductId")
REFERENCES "Products" ("Id");

