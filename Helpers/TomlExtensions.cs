using Tomlyn.Model;

namespace FrpManager.Helpers
{
    /// <summary>
    /// Extension methods for Tomlyn TOML parsing to simplify value retrieval.
    /// </summary>
    public static class TomlExtensions
    {
        /// <summary>
        /// Safely retrieves a string value from a TOML table.
        /// Returns null if the key doesn't exist or the value is null.
        /// </summary>
        /// <param name="t">The TOML table to search.</param>
        /// <param name="key">The key to look up.</param>
        /// <returns>The value as a string, or null if not found.</returns>
        public static string? TryGet(this TomlTable t, string key)
            => t.TryGetValue(key, out var v) ? v?.ToString() : null;
    }
}
