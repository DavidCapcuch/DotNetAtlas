using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SagaOrchestrators.Common.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class CapturePivotSagaCaptureApprovalTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refund_timeout_token_id",
                schema: "saga",
                table: "payment_processing_saga_state");

            migrationBuilder.DropColumn(
                name: "success_finalization_timeout_token_id",
                schema: "saga",
                table: "payment_processing_saga_state");

            migrationBuilder.AddColumn<Guid>(
                name: "capture_approval_timeout_token_id",
                schema: "saga",
                table: "payment_processing_saga_state",
                type: "uuid",
                nullable: true,
                comment: "Token ID for capture-approval wait-state timeout scheduler - set when schedule is active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "capture_approval_timeout_token_id",
                schema: "saga",
                table: "payment_processing_saga_state");

            migrationBuilder.AddColumn<Guid>(
                name: "refund_timeout_token_id",
                schema: "saga",
                table: "payment_processing_saga_state",
                type: "uuid",
                nullable: true,
                comment: "Token ID for refund timeout scheduler - set when schedule is active");

            migrationBuilder.AddColumn<Guid>(
                name: "success_finalization_timeout_token_id",
                schema: "saga",
                table: "payment_processing_saga_state",
                type: "uuid",
                nullable: true,
                comment: "Token ID for success finalization timeout scheduler - set when schedule is active");
        }
    }
}
