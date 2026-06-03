
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM payments."__EFMigrationsHistory" WHERE "migration_id" = '20260603171058_MovePaymentUniqueConstraintToOrderId') THEN
    DROP INDEX payments.ix_payment_transactions_order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM payments."__EFMigrationsHistory" WHERE "migration_id" = '20260603171058_MovePaymentUniqueConstraintToOrderId') THEN
    DROP INDEX payments.ux_payment_transactions_correlation_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM payments."__EFMigrationsHistory" WHERE "migration_id" = '20260603171058_MovePaymentUniqueConstraintToOrderId') THEN
    COMMENT ON COLUMN payments.payment_transactions.order_id IS 'Ordering aggregate id this payment is attached to (frozen at creation). Unique index enforces one payment per order (ADR-0029).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM payments."__EFMigrationsHistory" WHERE "migration_id" = '20260603171058_MovePaymentUniqueConstraintToOrderId') THEN
    COMMENT ON COLUMN payments.payment_transactions.correlation_id IS 'Originating saga correlation id (== OrderId per ADR-0029; links checkout / order / invoice).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM payments."__EFMigrationsHistory" WHERE "migration_id" = '20260603171058_MovePaymentUniqueConstraintToOrderId') THEN
    COMMENT ON COLUMN payments.payment_transactions.id IS 'Primary key — saga-minted UUID v7 (time-ordered), carried on AuthorizePaymentCommand as PaymentTransactionId; distinct from the saga key (OrderId). One payment per order is enforced by the ux_payment_transactions_order_id unique index (ADR-0029). See docs/bc-design/payments.md § 2.2 (I-7).';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM payments."__EFMigrationsHistory" WHERE "migration_id" = '20260603171058_MovePaymentUniqueConstraintToOrderId') THEN
    CREATE UNIQUE INDEX ux_payment_transactions_order_id ON payments.payment_transactions (order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM payments."__EFMigrationsHistory" WHERE "migration_id" = '20260603171058_MovePaymentUniqueConstraintToOrderId') THEN
    INSERT INTO payments."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260603171058_MovePaymentUniqueConstraintToOrderId', '10.0.8');
    END IF;
END $EF$;
