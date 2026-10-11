using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BrokerLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClientsAndHistoryOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "sequence",
                table: "journal_entries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);

            migrationBuilder.CreateTable(
                name: "clients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clients", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_sequence",
                table: "journal_entries",
                column: "sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_client_id",
                table: "accounts",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_clients_email",
                table: "clients",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clients");

            migrationBuilder.DropIndex(
                name: "ix_journal_entries_sequence",
                table: "journal_entries");

            migrationBuilder.DropIndex(
                name: "ix_accounts_client_id",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "sequence",
                table: "journal_entries");
        }
    }
}
