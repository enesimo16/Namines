PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS "Categories" (
    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "ParentId" INTEGER NULL,
    FOREIGN KEY ("ParentId") REFERENCES "Categories" ("Id")
);

