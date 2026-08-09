CREATE TABLE "Categories" (
    "Id" NUMBER(10) GENERATED ALWAYS AS IDENTITY NOT NULL,
    "Name" NVARCHAR2(120) NOT NULL,
    "ParentId" NUMBER(10) NULL,
    CONSTRAINT "PK_Categories" PRIMARY KEY ("Id")
);

ALTER TABLE "Categories"
    ADD CONSTRAINT "FK_Categories_Categories_Paren"
    FOREIGN KEY ("ParentId")
    REFERENCES "Categories" ("Id")
    ON DELETE CASCADE;

