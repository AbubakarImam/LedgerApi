using LedgerApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerApi.Data;

public class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionChain> TransactionChains => Set<TransactionChain>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Reversal> Reversals => Set<Reversal>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => a.AccountNumber).IsUnique();
            // Keys are scoped to the calling client, same as transactions (decision #42). Partial: accounts created
            // before decision #43 (including the seeded funding accounts) have no key and are left out, since with
            // nulls not distinct they would all collide as (null, null). Every new account has a key.
            entity.HasIndex(a => new { a.InitiatedBy, a.IdempotencyKey }).IsUnique().AreNullsDistinct(false)
                .HasFilter("\"idempotency_key\" IS NOT NULL");
            entity.Property(a => a.AccountType).HasConversion<string>();
            entity.Property(a => a.AccountClass).HasConversion<string>();
            entity.Property(a => a.Status).HasConversion<string>();
            entity.ToTable(t => t.HasCheckConstraint("ck_accounts_account_class", "\"account_class\" IN ('Customer', 'System')"));
            entity.ToTable(t => t.HasCheckConstraint("ck_accounts_status", "\"status\" IN ('Active', 'Frozen', 'Blocked')"));
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => t.Reference).IsUnique();
            // Keys are scoped to the calling client (decision #42). Nulls are not distinct, so rows from before
            // initiated_by was recorded still cannot share a key with each other.
            entity.HasIndex(t => new { t.InitiatedBy, t.IdempotencyKey }).IsUnique().AreNullsDistinct(false);
            entity.Property(t => t.Status).HasConversion<string>();
            entity.ToTable(t => t.HasCheckConstraint("ck_transactions_status", "\"status\" IN ('Pending', 'Success', 'Failed')"));

            // Chain legs (decision #45): a leg has all three chain fields or none, a leg position is unique
            // within its chain, and the purpose is one of the known values.
            entity.Property(t => t.Purpose).HasConversion<string>();
            entity.HasIndex(t => new { t.ChainId, t.Sequence }).IsUnique();
            entity.ToTable(t => t.HasCheckConstraint("ck_transactions_purpose", "\"purpose\" IN ('Principal', 'Fee', 'Tax')"));
            entity.ToTable(t => t.HasCheckConstraint("ck_transactions_chain_leg",
                "(\"chain_id\" IS NULL AND \"sequence\" IS NULL AND \"purpose\" IS NULL) OR " +
                "(\"chain_id\" IS NOT NULL AND \"sequence\" >= 1 AND \"purpose\" IS NOT NULL)"));
            entity.HasOne(t => t.Chain)
                .WithMany(c => c.Legs)
                .HasForeignKey(t => t.ChainId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TransactionChain>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.Reference).IsUnique();
            // Same per-client idempotency scheme as transactions (decision #42); caught by this index's name.
            entity.HasIndex(c => new { c.InitiatedBy, c.IdempotencyKey }).IsUnique().AreNullsDistinct(false);
            // A chain can be reversed at most once; caught by this index's name (AlreadyReversed, 409).
            entity.HasIndex(c => c.ReversesChainId).IsUnique();
            entity.Property(c => c.Status).HasConversion<string>();
            entity.ToTable(t => t.HasCheckConstraint("ck_transaction_chains_status", "\"status\" IN ('Pending', 'Success', 'Failed')"));
            entity.HasOne(c => c.ReversesChain)
                .WithMany()
                .HasForeignKey(c => c.ReversesChainId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntryType).HasConversion<string>();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.AccountId, e.CreatedAt });
            entity.ToTable(t => t.HasCheckConstraint("ck_ledger_entries_amount_positive", "\"amount\" > 0"));
            entity.ToTable(t => t.HasCheckConstraint("ck_ledger_entries_entry_type", "\"entry_type\" IN ('Debit', 'Credit')"));

            entity.HasOne(e => e.Transaction)
                .WithMany(t => t.Entries)
                .HasForeignKey(e => e.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Account)
                .WithMany(a => a.Entries)
                .HasForeignKey(e => e.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Reversal>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.OriginalTransactionId).IsUnique();

            entity.HasOne(r => r.OriginalTransaction)
                .WithMany()
                .HasForeignKey(r => r.OriginalTransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.ReversalTransaction)
                .WithMany()
                .HasForeignKey(r => r.ReversalTransactionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Amount).HasPrecision(18, 2);
            entity.HasIndex(a => a.EntityId);
            entity.HasIndex(a => a.CorrelationId);
        });
    }
}
