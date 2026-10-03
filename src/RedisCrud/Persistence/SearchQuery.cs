using System.Globalization;
using System.Text;

namespace RedisCrud.Persistence;

/// <summary>
/// Builds Redis Query Engine (FT.SEARCH, DIALECT 2) query strings.
/// User text is never passed through: it is split into letter/digit tokens, so query syntax
/// (<c>| - @ { } ( ) *</c> ...) cannot be injected.
/// </summary>
internal static class SearchQuery
{
    public const string ActiveFilter = "@IsDeleted:{false}";
    public const int MaxTerms = 8;
    public const int MaxTermLength = 64;

    /// <summary>Splits free text into safe search terms.</summary>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var terms = new List<string>();
        var current = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
                continue;
            }
            Flush();
        }
        Flush();
        return terms;

        void Flush()
        {
            if (current.Length > 0 && terms.Count < MaxTerms)
                terms.Add(current.Length > MaxTermLength ? current.ToString(0, MaxTermLength) : current.ToString());
            current.Clear();
        }
    }

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// Every term must match at least one searchable field (AND across terms).
    /// Terms of 2+ characters are prefix-matched, so "kalem" finds "kalemler": the default English
    /// stemmer does nothing for agglutinative languages such as Turkish, prefix matching does.
    /// Each term is OR-ed with its Turkish-cased forms, because Unicode case folding (which the
    /// Query Engine applies) maps I to i and never to ı: "KIRMIZI" would otherwise miss "kırmızı"
    /// and "istanbul" or "ISTANBUL" would miss "İstanbul". Fully ASCII-folded input ("ilik" for "ılık")
    /// is not handled: that needs a folded copy of the text in the index.
    /// Returns <c>null</c> when the text contains no searchable terms.
    /// </summary>
    public static string? BuildFullText(IReadOnlyList<string> searchableFields, string? text)
    {
        if (searchableFields.Count == 0)
            throw new InvalidOperationException("Entity has no [Searchable] properties.");

        var terms = Tokenize(text);
        if (terms.Count == 0)
            return null;

        string fields = string.Join('|', searchableFields);
        string expression = string.Join(' ', terms.Select(Term));
        return $"{ActiveFilter} @{fields}:({expression})";
    }

    private static string Term(string term)
    {
        string suffix = term.Length >= 2 ? "*" : string.Empty;
        var forms = new[] { term, term.ToLower(Turkish), term.ToUpper(Turkish), term.ToLowerInvariant().ToUpper(Turkish) }
            .Distinct(StringComparer.Ordinal)
            .Select(f => f + suffix)
            .ToArray();
        return forms.Length == 1 ? forms[0] : $"({string.Join('|', forms)})";
    }
}
