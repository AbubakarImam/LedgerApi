using System.Text.RegularExpressions;

namespace LedgerApi.Validation;

// One rule for every idempotency key: short enough to always fit the unique index (Postgres rejects index
// entries over ~2.7 KB with a 500), and limited to characters that are safe in logs and URLs.
public static partial class IdempotencyKeyRule
{
    public const int MaxLength = 128;

    // \z, not $: in .NET, $ also matches just before a trailing newline, so "abc\n" would pass.
    [GeneratedRegex(@"^[A-Za-z0-9._:-]{1,128}\z")]
    private static partial Regex Pattern();

    public static string? Validate(string? key) =>
        string.IsNullOrWhiteSpace(key) ? "IdempotencyKey is required."
        : Pattern().IsMatch(key) ? null
        : $"IdempotencyKey must be 1-{MaxLength} characters: letters, digits, '-', '_', '.' or ':'.";
}
