namespace Domain.Identity
{
    /// <summary>
    /// Canonical form of a user email: trimmed and lowercased. Emails are stored and looked up only in this form,
    /// so the case-sensitive unique index on Users.Email also enforces case-insensitive uniqueness.
    /// </summary>
    public static class EmailAddress
    {
        public static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();
    }
}
