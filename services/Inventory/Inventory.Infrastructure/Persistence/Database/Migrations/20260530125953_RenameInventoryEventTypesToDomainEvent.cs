using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class RenameInventoryEventTypesToDomainEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Forward-only rewrite of the persisted CLR-type discriminators on
            // inventory.stock_events. The internal ES events were renamed
            // *Event -> *DomainEvent to match the cross-BC convention; existing
            // rows must be relabelled so StockEventSerializer.EventTypeRegistry
            // resolves them at rehydration time. Pure data UPDATE — no schema
            // change to the table.
            migrationBuilder.Sql(
                "UPDATE inventory.stock_events SET event_type = 'StockItemInitializedDomainEvent' WHERE event_type = 'StockItemInitializedEvent';");
            migrationBuilder.Sql(
                "UPDATE inventory.stock_events SET event_type = 'StockReceivedDomainEvent' WHERE event_type = 'StockReceivedEvent';");
            migrationBuilder.Sql(
                "UPDATE inventory.stock_events SET event_type = 'StockReservedDomainEvent' WHERE event_type = 'StockReservedEvent';");
            migrationBuilder.Sql(
                "UPDATE inventory.stock_events SET event_type = 'ReservationConfirmedDomainEvent' WHERE event_type = 'ReservationConfirmedEvent';");
            migrationBuilder.Sql(
                "UPDATE inventory.stock_events SET event_type = 'ReservationReleasedDomainEvent' WHERE event_type = 'ReservationReleasedEvent';");
            migrationBuilder.Sql(
                "UPDATE inventory.stock_events SET event_type = 'StockAdjustedDomainEvent' WHERE event_type = 'StockAdjustedEvent';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE inventory.stock_events SET event_type = 'StockItemInitializedEvent' WHERE event_type = 'StockItemInitializedDomainEvent';");
            migrationBuilder.Sql(
                "UPDATE inventory.stock_events SET event_type = 'StockReceivedEvent' WHERE event_type = 'StockReceivedDomainEvent';");
            migrationBuilder.Sql(
                "UPDATE inventory.stock_events SET event_type = 'StockReservedEvent' WHERE event_type = 'StockReservedDomainEvent';");
            migrationBuilder.Sql(
                "UPDATE inventory.stock_events SET event_type = 'ReservationConfirmedEvent' WHERE event_type = 'ReservationConfirmedDomainEvent';");
            migrationBuilder.Sql(
                "UPDATE inventory.stock_events SET event_type = 'ReservationReleasedEvent' WHERE event_type = 'ReservationReleasedDomainEvent';");
            migrationBuilder.Sql(
                "UPDATE inventory.stock_events SET event_type = 'StockAdjustedEvent' WHERE event_type = 'StockAdjustedDomainEvent';");
        }
    }
}
