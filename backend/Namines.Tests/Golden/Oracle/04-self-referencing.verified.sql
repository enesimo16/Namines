CREATE TABLE "Categories" (
    "Id" NUMBER(10) GENERATED ALWAYS AS IDENTITY NOT NULL,
    "Name" NVARCHAR2(120) NOT NULL,
    "ParentId" NUMBER(10) NULL,
    CONSTRAINT "PK_Categories" PRIMARY KEY ("Id")
);

ALTER TABLE "Categories"
    ADD CONSTRAINT "FK_Categories_Categor_a2d9d449"
    FOREIGN KEY ("ParentId")
    REFERENCES "Categories" ("Id");

