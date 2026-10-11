using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrokerLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Orders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    side = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    limit_price = table.Column<decimal>(type: "numeric(20,2)", nullable: true),
                    quantity = table.Column<long>(type: "bigint", nullable: false),
                    blocked_amount = table.Column<decimal>(type: "numeric(20,2)", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    exchange_order_id = table.Column<long>(type: "bigint", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    funds_blocked = table.Column<bool>(type: "boolean", nullable: false),
                    funds_released = table.Column<bool>(type: "boolean", nullable: false),
                    submit_attempts = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_orders_released_only_if_blocked", "not funds_released or funds_blocked");
                    table.CheckConstraint("ck_orders_submit_attempts_not_negative", "submit_attempts >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_client_id_client_key",
                table: "orders",
                columns: new[] { "client_id", "client_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_client_id_created_at",
                table: "orders",
                columns: new[] { "client_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_needing_attention",
                table: "orders",
                column: "updated_at",
                filter: "status in ('Pending', 'Unknown') or (status = 'Rejected' and funds_blocked and not funds_released)");

            migrationBuilder.CreateIndex(
                name: "ix_orders_symbol_exchange_order_id",
                table: "orders",
                columns: new[] { "symbol", "exchange_order_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orders");
        }
    }
}
