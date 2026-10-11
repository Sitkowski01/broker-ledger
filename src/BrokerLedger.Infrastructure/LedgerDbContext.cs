using BrokerLedger.Domain;
using Microsoft.EntityFrameworkCore;

namespace BrokerLedger.Infrastructure;

internal sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    // Do grosza; 18 cyfr przed przecinkiem to milion zapisow po Posting.MaxAmount. Akcje (sztuki) tez sie mieszcza.
    private const string AmountType = "numeric(20,2)";

    /// <summary>Po tej nazwie PostgresLedgerStore rozpoznaje powtorke — dlatego nadana jawnie.</summary>
    public const string IdempotencyIndex = "ix_journal_entries_idempotency_key";

    /// <summary>Po tej nazwie rejestracja rozpoznaje zajety e-mail.</summary>
    public const string ClientEmailIndex = "ix_clients_email";

    public DbSet<AccountRow> Accounts => Set<AccountRow>();
    public DbSet<JournalEntryRow> JournalEntries => Set<JournalEntryRow>();
    public DbSet<PostingRow> Postings => Set<PostingRow>();
    public DbSet<ClientRow> Clients => Set<ClientRow>();
    public DbSet<OrderRow> Orders => Set<OrderRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountRow>(account =>
        {
            account.Property(a => a.Type).HasConversion<string>().HasMaxLength(32);
            account.Property(a => a.AssetKind).HasConversion<string>().HasMaxLength(16);
            account.Property(a => a.AssetCode).HasMaxLength(12);
            account.Property(a => a.NormalSide).HasConversion<string>().HasMaxLength(8);
            account.Property(a => a.Balance).HasColumnType(AmountType);
            // Konto domu maklerskiego nie ma klienta (NULL) — mimo to ma byc jedno, stad NULLS NOT DISTINCT.
            account.HasIndex(a => new { a.Type, a.AssetKind, a.AssetCode, a.ClientId })
                .IsUnique().AreNullsDistinct(false);
            // Salda i historia klienta szukaja kont po kliencie — bez tego skan calej tabeli.
            account.HasIndex(a => a.ClientId);
            // Ostatnia linia obrony: nawet blad w kodzie nie zapisze ujemnego salda.
            account.ToTable(t => t.HasCheckConstraint("ck_accounts_balance_not_negative", "balance >= 0"));
        });

        modelBuilder.Entity<JournalEntryRow>(entry =>
        {
            entry.Property(e => e.IdempotencyKey).HasMaxLength(ILedgerStore.MaxIdempotencyKeyLength);
            entry.Property(e => e.Description).HasMaxLength(JournalEntry.MaxDescriptionLength);
            entry.HasIndex(e => e.IdempotencyKey).IsUnique().HasDatabaseName(IdempotencyIndex);
            entry.Property(e => e.Sequence).UseIdentityAlwaysColumn();
            entry.HasIndex(e => e.Sequence).IsUnique();
            entry.HasMany(e => e.Postings).WithOne().HasForeignKey(p => p.EntryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PostingRow>(posting =>
        {
            posting.Property(p => p.Side).HasConversion<string>().HasMaxLength(8);
            posting.Property(p => p.Amount).HasColumnType(AmountType);
            posting.HasOne(p => p.Account).WithMany().HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Restrict);
            posting.HasIndex(p => new { p.EntryId, p.AccountId }).IsUnique();
            posting.ToTable(t => t.HasCheckConstraint("ck_postings_amount_positive", "amount > 0"));
        });

        modelBuilder.Entity<ClientRow>(client =>
        {
            client.Property(c => c.Email).HasMaxLength(EmailAddress.MaxLength);
            client.Property(c => c.PasswordHash).HasMaxLength(500);
            client.HasIndex(c => c.Email).IsUnique().HasDatabaseName(ClientEmailIndex);
        });

        modelBuilder.Entity<OrderRow>(order =>
        {
            order.Property(o => o.ClientKey).HasMaxLength(Order.MaxClientKeyLength);
            order.Property(o => o.Symbol).HasMaxLength(12);
            order.Property(o => o.Side).HasConversion<string>().HasMaxLength(4);
            order.Property(o => o.Type).HasConversion<string>().HasMaxLength(8);
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
            order.Property(o => o.LimitPrice).HasColumnType(AmountType);
            order.Property(o => o.BlockedAmount).HasColumnType(AmountType);
            order.Property(o => o.RejectionReason).HasMaxLength(200);
            // Powtorka zadania z tym samym Idempotency-Key trafia na to samo zlecenie.
            order.HasIndex(o => new { o.ClientId, o.ClientKey }).IsUnique();
            order.HasIndex(o => new { o.ClientId, o.CreatedAt });
            // Reconciler pyta tylko o niedokonczone zlecenia — maly indeks zamiast skanu calej historii.
            order.HasIndex(o => o.UpdatedAt).HasDatabaseName("ix_orders_needing_attention")
                .HasFilter("status in ('Pending', 'Unknown') or (status = 'Rejected' and funds_blocked and not funds_released)");
            // Rozliczenie transakcji z gieldy szuka zlecenia po jej numerze.
            order.HasIndex(o => new { o.Symbol, o.ExchangeOrderId });
            order.ToTable(t =>
            {
                t.HasCheckConstraint("ck_orders_quantity_positive", "quantity > 0");
                t.HasCheckConstraint("ck_orders_released_only_if_blocked", "not funds_released or funds_blocked");
                t.HasCheckConstraint("ck_orders_submit_attempts_not_negative", "submit_attempts >= 0");
            });
        });
    }
}
