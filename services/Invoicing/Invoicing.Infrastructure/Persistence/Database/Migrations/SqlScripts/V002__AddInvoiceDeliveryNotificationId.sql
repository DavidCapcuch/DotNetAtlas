
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604211624_AddInvoiceDeliveryNotificationId') THEN
    ALTER TABLE invoicing.invoices ADD delivery_notification_id uuid;
    COMMENT ON COLUMN invoicing.invoices.delivery_notification_id IS 'NotificationId (ADR-0031) minted when delivery was requested; correlates the delivery confirmation. Null until Issued with a delivery channel.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604211624_AddInvoiceDeliveryNotificationId') THEN
    CREATE UNIQUE INDEX ux_invoices_delivery_notification_id ON invoicing.invoices (delivery_notification_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM invoicing."__EFMigrationsHistory" WHERE "migration_id" = '20260604211624_AddInvoiceDeliveryNotificationId') THEN
    INSERT INTO invoicing."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260604211624_AddInvoiceDeliveryNotificationId', '10.0.8');
    END IF;
END $EF$;
