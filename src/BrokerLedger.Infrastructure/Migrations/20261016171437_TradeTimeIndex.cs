using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrokerLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TradeTimeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_exchange_inbox_trade_time",
                table: "exchange_inbox",
                columns: new[] { "symbol", "received_at" },
                filter: "type = 'TRADE'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_exchange_inbox_trade_time",
                table: "exchange_inbox");
        }
    }
}
