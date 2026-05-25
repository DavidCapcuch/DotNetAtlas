
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM payments."__EFMigrationsHistory" WHERE "migration_id" = '20260522094454_Add_VoidReason_To_PaymentTransactions') THEN
    ALTER TABLE payments.payment_transactions ADD void_reason character varying(256);
    COMMENT ON COLUMN payments.payment_transactions.void_reason IS 'Saga-supplied reason for the void (H-5 closeout; nullable until Void succeeds).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM payments."__EFMigrationsHistory" WHERE "migration_id" = '20260522094454_Add_VoidReason_To_PaymentTransactions') THEN
    INSERT INTO payments."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260522094454_Add_VoidReason_To_PaymentTransactions', '10.0.8');
    END IF;
END $EF$;
