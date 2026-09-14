CREATE TABLE "Orders" (
    "Id" SERIAL NOT NULL,
    "Total" numeric NOT NULL
    , CONSTRAINT "PK_Orders" PRIMARY KEY ("Id")
);

-- Trigger: trg_order_audit (After Insert)
CREATE OR REPLACE FUNCTION audit_order() RETURNS TRIGGER AS $$
BEGIN
  RAISE NOTICE 'order %', NEW."Id";
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_order_audit AFTER INSERT ON "Orders"
FOR EACH ROW EXECUTE FUNCTION audit_order();

