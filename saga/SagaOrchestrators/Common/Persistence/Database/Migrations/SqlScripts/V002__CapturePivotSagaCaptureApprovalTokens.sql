
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602093928_CapturePivotSagaCaptureApprovalTokens') THEN
    ALTER TABLE saga.payment_processing_saga_state DROP COLUMN refund_timeout_token_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602093928_CapturePivotSagaCaptureApprovalTokens') THEN
    ALTER TABLE saga.payment_processing_saga_state DROP COLUMN success_finalization_timeout_token_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602093928_CapturePivotSagaCaptureApprovalTokens') THEN
    ALTER TABLE saga.payment_processing_saga_state ADD capture_approval_timeout_token_id uuid;
    COMMENT ON COLUMN saga.payment_processing_saga_state.capture_approval_timeout_token_id IS 'Token ID for capture-approval wait-state timeout scheduler - set when schedule is active';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM saga."__EFMigrationsHistory" WHERE "migration_id" = '20260602093928_CapturePivotSagaCaptureApprovalTokens') THEN
    INSERT INTO saga."__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260602093928_CapturePivotSagaCaptureApprovalTokens', '10.0.8');
    END IF;
END $EF$;
