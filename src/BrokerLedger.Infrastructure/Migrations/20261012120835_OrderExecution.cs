using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrokerLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrderExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_needing_attention",
                table: "orders");

            migrationBuilder.AddColumn<decimal>(
                name: "filled_commission",
                table: "orders",
                type: "numeric(20,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "filled_quantity",
                table: "orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "filled_value",
                table: "orders",
                type: "numeric(20,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "last_event_run_id",
                table: "orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "last_event_sequence",
                table: "orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_needing_attention",
                table: "orders",
                column: "updated_at",
                filter: "status in ('Pending', 'Unknown') or (status in ('Rejected', 'Filled', 'Cancelled') and funds_blocked and not funds_released)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_filled_within_quantity",
                table: "orders",
                sql: "filled_quantity between 0 and quantity");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_last_event_complete",
                table: "orders",
                sql: "(last_event_run_id is null) = (last_event_sequence is null)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_needing_attention",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_filled_within_quantity",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_last_event_complete",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "filled_commission",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "filled_quantity",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "filled_value",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "last_event_run_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "last_event_sequence",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "ix_orders_needing_attention",
                table: "orders",
                column: "updated_at",
                filter: "status in ('Pending', 'Unknown') or (status = 'Rejected' and funds_blocked and not funds_released)");
        }
    }
}
