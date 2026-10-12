using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BrokerLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExchangeInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exchange_dead_letters",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    message = table.Column<string>(type: "text", nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_dead_letters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_inbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    maker_order_id = table.Column<long>(type: "bigint", nullable: true),
                    taker_order_id = table.Column<long>(type: "bigint", nullable: true),
                    taker_side = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    price = table.Column<decimal>(type: "numeric(20,2)", nullable: true),
                    order_id = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    quantity = table.Column<long>(type: "bigint", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_inbox", x => x.id);
                    table.CheckConstraint("ck_exchange_inbox_cancelled", "type <> 'CANCELLED' or (order_id is not null and reason is not null and maker_order_id is null)");
                    table.CheckConstraint("ck_exchange_inbox_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_exchange_inbox_trade", "type <> 'TRADE' or (maker_order_id is not null and taker_order_id is not null and taker_side is not null and price is not null and price > 0 and order_id is null)");
                    table.CheckConstraint("ck_exchange_inbox_type", "type in ('TRADE', 'CANCELLED')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_inbox_pending",
                table: "exchange_inbox",
                column: "id",
                filter: "settled_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_exchange_inbox_run_id_symbol_sequence",
                table: "exchange_inbox",
                columns: new[] { "run_id", "symbol", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exchange_dead_letters");

            migrationBuilder.DropTable(
                name: "exchange_inbox");
        }
    }
}
