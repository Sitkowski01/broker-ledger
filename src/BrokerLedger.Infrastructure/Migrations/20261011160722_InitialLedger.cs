using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BrokerLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    asset_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    asset_code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    normal_side = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(20,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_balance_not_negative", "balance >= 0");
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "postings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    side = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(20,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_postings", x => x.id);
                    table.CheckConstraint("ck_postings_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_postings_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_postings_journal_entries_entry_id",
                        column: x => x.entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_type_asset_kind_asset_code_client_id",
                table: "accounts",
                columns: new[] { "type", "asset_kind", "asset_code", "client_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_idempotency_key",
                table: "journal_entries",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_postings_account_id",
                table: "postings",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_postings_entry_id_account_id",
                table: "postings",
                columns: new[] { "entry_id", "account_id" },
                unique: true);

            // Ksiega tylko sie dopisuje: blad poprawia sie kolejnym dokumentem, nie edycja historii.
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_ledger_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'ledger is append-only: % on % is not allowed', TG_OP, TG_TABLE_NAME;
                END;
                $$;

                CREATE TRIGGER journal_entries_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON journal_entries
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_ledger_change();
                CREATE TRIGGER postings_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON postings
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_ledger_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER postings_append_only ON postings;
                DROP TRIGGER journal_entries_append_only ON journal_entries;
                DROP FUNCTION reject_ledger_change();
                """);

            migrationBuilder.DropTable(
                name: "postings");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "journal_entries");
        }
    }
}
