using Tomlyn.Model;

namespace FrpManager.Helpers
{
    public static class TomlExtensions
    {
        public static string? TryGet(this TomlTable t, string key)
            => t.TryGetValue(key, out var v) ? v?.ToString() : null;
    }
}