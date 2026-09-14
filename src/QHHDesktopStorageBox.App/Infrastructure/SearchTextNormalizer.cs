using System.Collections.Concurrent;
using TinyPinyin;

namespace QHHDesktopStorageBox.App.Infrastructure;

public static class SearchTextNormalizer
{
    private const int MaximumCacheEntries = 8192;
    private static readonly ConcurrentDictionary<string, SearchIndexEntry> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool Matches(
        string query,
        string? first,
        string? second = null,
        string? third = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var normalizedQuery = query.Trim();
        return MatchesValue(normalizedQuery, first)
            || MatchesValue(normalizedQuery, second)
            || MatchesValue(normalizedQuery, third);
    }

    private static bool MatchesValue(string query, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var index = Cache.GetOrAdd(value, BuildIndex);
        if (index.Pinyin.Contains(query, StringComparison.OrdinalIgnoreCase)
            || index.Initials.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Cache.Count > MaximumCacheEntries)
        {
            Cache.Clear();
        }

        return false;
    }

    private static SearchIndexEntry BuildIndex(string value)
    {
        try
        {
            return new SearchIndexEntry(
                PinyinHelper.GetPinyin(value, string.Empty),
                PinyinHelper.GetPinyinInitials(value));
        }
        catch
        {
            return new SearchIndexEntry(value, value);
        }
    }

    private sealed record SearchIndexEntry(string Pinyin, string Initials);
}
