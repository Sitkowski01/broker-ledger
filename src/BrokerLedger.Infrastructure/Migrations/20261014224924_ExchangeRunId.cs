using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrokerLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExchangeRunId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "exchange_run_id",
                table: "orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_run_only_with_number",
                table: "orders",
                sql: "exchange_run_id is null or exchange_order_id is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_run_only_with_number",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "exchange_run_id",
                table: "orders");
        }
    }
}
