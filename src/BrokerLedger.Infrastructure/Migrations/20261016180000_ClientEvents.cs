using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrokerLedger.Infrastructure.Migrations
{
    /// <summary>
    /// Wyzwalacze, ktore po kazdej zmianie zlecenia i kazdym zapisie na koncie klienta wysylaja NOTIFY.
    /// Baza sama mowi, ze cos sie zmienilo — niezaleznie od tego, czy zmiana przyszla z zadania HTTP, reconcilera,
    /// czy rozliczenia z Kafki, i ktora instancja API ja zrobila. NOTIFY idzie dopiero przy COMMIT, a identyczne
    /// powiadomienia w jednej transakcji Postgres sklada w jedno (dokument z kilkoma zapisami = jeden sygnal).
    /// Model sie nie zmienia, wiec bez pliku Designer.
    /// </summary>
    [DbContext(typeof(LedgerDbContext))]
    [Migration("20261016180000_ClientEvents")]
    public partial class ClientEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION notify_client_event() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    owner uuid;
                BEGIN
                    IF TG_TABLE_NAME = 'orders' THEN
                        PERFORM pg_notify('client_events', json_build_object(
                            'client', NEW.client_id, 'kind', 'order', 'order', NEW.id, 'status', NEW.status)::text);
                    ELSE
                        SELECT a.client_id INTO owner FROM accounts a WHERE a.id = NEW.account_id;
                        IF owner IS NOT NULL THEN
                            PERFORM pg_notify('client_events', json_build_object('client', owner, 'kind', 'ledger')::text);
                        END IF;
                    END IF;
                    RETURN NULL;
                END;
                $$;

                CREATE TRIGGER orders_notify AFTER INSERT OR UPDATE ON orders
                    FOR EACH ROW EXECUTE FUNCTION notify_client_event();
                CREATE TRIGGER postings_notify AFTER INSERT ON postings
                    FOR EACH ROW EXECUTE FUNCTION notify_client_event();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER postings_notify ON postings;
                DROP TRIGGER orders_notify ON orders;
                DROP FUNCTION notify_client_event();
                """);
        }
    }
}
