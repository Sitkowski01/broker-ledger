using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrokerLedger.Infrastructure.Migrations
{
    /// <summary>
    /// Czas zdarzenia na gieldzie obok czasu odbioru. Ceny w chwili z przeszlosci (wykres wyniku) szly po czasie odbioru,
    /// wiec nadrabianie po przerwie dawalo calej zaleglej historii czas "teraz". Stare wiersze dostaja czas odbioru —
    /// lepszego broker nie zapisal.
    /// </summary>
    public partial class TradeOccurredAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_exchange_inbox_trade_time",
                table: "exchange_inbox");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "occurred_at",
                table: "exchange_inbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE exchange_inbox SET occurred_at = received_at;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "occurred_at",
                table: "exchange_inbox",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_exchange_inbox_trade_time",
                table: "exchange_inbox",
                columns: new[] { "symbol", "occurred_at" },
                filter: "type = 'TRADE'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_exchange_inbox_trade_time",
                table: "exchange_inbox");

            migrationBuilder.DropColumn(
                name: "occurred_at",
                table: "exchange_inbox");

            migrationBuilder.CreateIndex(
                name: "ix_exchange_inbox_trade_time",
                table: "exchange_inbox",
                columns: new[] { "symbol", "received_at" },
                filter: "type = 'TRADE'");
        }
    }
}
