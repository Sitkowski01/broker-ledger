using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrokerLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LastTradeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_exchange_inbox_last_trade",
                table: "exchange_inbox",
                columns: new[] { "symbol", "id" },
                descending: new[] { false, true },
                filter: "type = 'TRADE'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_exchange_inbox_last_trade",
                table: "exchange_inbox");
        }
    }
}
