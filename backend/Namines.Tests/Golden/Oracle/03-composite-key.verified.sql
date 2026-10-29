CREATE TABLE "Orders" (
    "Id" NUMBER(10) GENERATED ALWAYS AS IDENTITY NOT NULL,
    CONSTRAINT "PK_Orders" PRIMARY KEY ("Id")
);

CREATE TABLE "Products" (
    "Id" NUMBER(10) GENERATED ALWAYS AS IDENTITY NOT NULL,
    CONSTRAINT "PK_Products" PRIMARY KEY ("Id")
);

CREATE TABLE "OrderProducts" (
    "OrderId" NUMBER(10) NOT NULL,
    "ProductId" NUMBER(10) NOT NULL,
    "Quantity" NUMBER(10) NOT NULL,
    CONSTRAINT "PK_OrderProducts" PRIMARY KEY ("OrderId", "ProductId")
);

ALTER TABLE "OrderProducts"
    ADD CONSTRAINT "FK_OrderProducts_Orde_31b3d9e8"
    FOREIGN KEY ("OrderId")
    REFERENCES "Orders" ("Id");

ALTER TABLE "OrderProducts"
    ADD CONSTRAINT "FK_OrderProducts_Prod_4963638b"
    FOREIGN KEY ("ProductId")
    REFERENCES "Products" ("Id");

