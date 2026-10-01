using RedisCrud.Persistence;

namespace RedisCrud.Tests.Unit;

public class SearchQueryTests
{
    [Theory]
    [InlineData("x)|(@IsDeleted:{true}", new[] { "x", "IsDeleted", "true" })]
    [InlineData("-kalem", new[] { "kalem" })]
    [InlineData("a*b ~c \"d\" @e:{f}", new[] { "a", "b", "c", "d", "e", "f" })]
    [InlineData("Kırmızı İstanbul ğüşöç", new[] { "Kırmızı", "İstanbul", "ğüşöç" })]
    public void Tokenize_keeps_only_letters_and_digits(string input, string[] expected) =>
        SearchQuery.Tokenize(input).Should().Equal(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("()|@{}*")]
    public void Text_without_terms_produces_no_query(string? input) =>
        SearchQuery.BuildFullText(["Description"], input).Should().BeNull();

    [Fact]
    public void Terms_are_capped() =>
        SearchQuery.Tokenize(string.Join(' ', Enumerable.Range(0, 50).Select(i => $"t{i}"))).Should().HaveCount(SearchQuery.MaxTerms);

    [Fact]
    public void Long_terms_are_truncated() =>
        SearchQuery.Tokenize(new string('a', 500)).Single().Should().HaveLength(SearchQuery.MaxTermLength);

    [Fact]
    public void Builds_prefix_query_over_searchable_fields_with_active_filter() =>
        SearchQuery.BuildFullText(["Description", "Notes"], "kalem k")
            .Should().Be("@IsDeleted:{false} @Description|Notes:((kalem*|KALEM*) (k|K))");

    [Theory]
    [InlineData("KIRMIZI", "(KIRMIZI*|kırmızı*|KİRMİZİ*)")]
    [InlineData("istanbul", "(istanbul*|İSTANBUL*)")]
    [InlineData("ISTANBUL", "(ISTANBUL*|ıstanbul*|İSTANBUL*)")]
    [InlineData("42", "42*")]
    public void Adds_turkish_case_variants_only_when_they_differ(string term, string expected) =>
        SearchQuery.BuildFullText(["D"], term).Should().Be($"@IsDeleted:{{false}} @D:({expected})");

    [Fact]
    public void Requires_searchable_fields() =>
        FluentActions.Invoking(() => SearchQuery.BuildFullText([], "x")).Should().Throw<InvalidOperationException>();
}
