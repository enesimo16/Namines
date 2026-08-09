CREATE TABLE "Categories" (
    "Id" SERIAL NOT NULL,
    "Name" NVARCHAR(120) NOT NULL,
    "ParentId" INT NULL
    , CONSTRAINT "PK_Categories" PRIMARY KEY ("Id")
);

ALTER TABLE "Categories" ADD CONSTRAINT "FK_Categories_Categories_ParentId" FOREIGN KEY("ParentId")
REFERENCES "Categories" ("Id");

