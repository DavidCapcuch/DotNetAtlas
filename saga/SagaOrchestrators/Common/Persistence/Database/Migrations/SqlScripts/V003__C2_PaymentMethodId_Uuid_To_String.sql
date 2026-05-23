DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260522121755_C2_PaymentMethodId_Uuid_To_String') THEN
    ALTER TABLE saga."PaymentProcessingSagaState" ALTER COLUMN payment_method_id TYPE character varying(64);
    COMMENT ON COLUMN saga."PaymentProcessingSagaState".payment_method_id IS 'Gateway-issued opaque payment-method token (Stripe ''pm_*'', Adyen alphanumeric, …); 1-64 chars. Changed from uuid in the Wave-1 closeout C-2 fix.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260522121755_C2_PaymentMethodId_Uuid_To_String') THEN
    COMMENT ON COLUMN saga."CheckoutSagaState".payment_method_id IS 'Saved payment method id (Guid). Stored as uuid because Basket + Ordering wire shapes still use Guid; CheckoutSaga string-encodes it only at the Payments-emit boundary (C-2 closeout — Payments-side schema changed, upstream BC wire shapes deferred).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260522121755_C2_PaymentMethodId_Uuid_To_String') THEN
    INSERT INTO saga."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260522121755_C2_PaymentMethodId_Uuid_To_String', '10.0.8');
    END IF;
END $EF$;

