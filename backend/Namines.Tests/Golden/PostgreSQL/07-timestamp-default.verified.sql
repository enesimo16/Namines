CREATE TABLE "events" (
    "id" SERIAL NOT NULL,
    "name" varchar(120) NOT NULL,
    "created_at" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "last_seen_at" timestamp NULL
    , CONSTRAINT "PK_events" PRIMARY KEY ("id")
);

