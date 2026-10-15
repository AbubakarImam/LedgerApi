using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <summary>
    /// Fills in the calling client on transactions from before decision #42 (decision #44).
    /// </summary>
    public partial class BackfillTransactionInitiatedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Transactions from before decision #42 have no client, so an authenticated retry of their key
            // did not match them and created a second transaction. Their client is recoverable from the audit
            // trail, which records the API client as actor_id for every transfer and reversal since auth existed.
            migrationBuilder.Sql("""
                UPDATE transactions AS t
                SET initiated_by = a.actor_id
                FROM (
                    SELECT DISTINCT ON (entity_id) entity_id, actor_id
                    FROM audit_logs
                    WHERE entity_type = 'Transaction' AND actor_id IS NOT NULL
                    ORDER BY entity_id, id
                ) AS a
                WHERE t.initiated_by IS NULL
                  AND t.reference = a.entity_id
                  -- Skip a row if that client already has a transaction with the same key (a duplicate the bug
                  -- already created); setting it would violate the unique (initiated_by, idempotency_key) index.
                  AND NOT EXISTS (
                      SELECT 1 FROM transactions AS t2
                      WHERE t2.initiated_by = a.actor_id AND t2.idempotency_key = t.idempotency_key);
                """);
        }

        /// <inheritdoc />
        // Not reversible: after the update, a client filled in here can't be told apart from one recorded at the time.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
