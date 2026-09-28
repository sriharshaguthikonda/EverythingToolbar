using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class SafeLiteralClassifierTests
    {
        private readonly SafeLiteralClassifier _classifier = new();

        [Theory]
        [InlineData("ext:pdf")]
        [InlineData("ext:pdf dm:thisyear")]
        [InlineData("size:>100mb")]
        [InlineData("!draft")]
        [InlineData("-draft")]
        [InlineData("<foo|bar>")]
        [InlineData("regex:foo.*bar")]
        [InlineData("path:\"C:\\Tools\"")]
        [InlineData("*.pdf")]
        [InlineData("\"C:\\Program Files\"")]
        [InlineData("foo*bar")]
        [InlineData("C:\\Tools\\app.exe")]
        [InlineData("100mb")]
        [InlineData("2024-01-01")]
        public void Classify_StructuralTerms_AreNeverPlainLiterals(string query)
        {
            var terms = _classifier.Classify(query);

            Assert.NotEmpty(terms);
            Assert.All(terms, t => Assert.Equal(SearchTermKind.UnsafeOrStructural, t.Kind));
            Assert.All(terms, t => Assert.False(SafeLiteralClassifier.IsCorrectable(t)));
        }

        [Theory]
        [InlineData("and")]
        [InlineData("OR")]
        [InlineData("Not")]
        public void Classify_BooleanOperators_AreNeverPlainLiterals(string keyword)
        {
            var terms = _classifier.Classify(keyword);

            var term = Assert.Single(terms);
            Assert.Equal(SearchTermKind.UnsafeOrStructural, term.Kind);
        }

        [Theory]
        [InlineData("neurosicence")]
        [InlineData("clinical")]
        [InlineData("attachement")]
        [InlineData("repo_map")]
        [InlineData("repo-map")]
        [InlineData("qwen3")]
        [InlineData("PowerToys")]
        [InlineData("naïve")]
        public void Classify_PlainFilenameLiterals_AreCorrectable(string query)
        {
            var terms = _classifier.Classify(query);

            var term = Assert.Single(terms);
            Assert.Equal(SearchTermKind.PlainLiteral, term.Kind);
            Assert.True(SafeLiteralClassifier.IsCorrectable(term));
        }

        [Fact]
        public void Classify_MixedQuery_SplitsByKind()
        {
            var terms = _classifier.Classify("clinical attachement ext:pdf");

            Assert.Equal(3, terms.Count);
            Assert.Equal(SearchTermKind.PlainLiteral, terms[0].Kind);
            Assert.Equal(SearchTermKind.PlainLiteral, terms[1].Kind);
            Assert.Equal(SearchTermKind.UnsafeOrStructural, terms[2].Kind);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("to")]
        public void Classify_ShortPlainLiterals_AreNotCorrectable(string query)
        {
            var terms = _classifier.Classify(query);

            var term = Assert.Single(terms);
            Assert.Equal(SearchTermKind.PlainLiteral, term.Kind);
            Assert.False(SafeLiteralClassifier.IsCorrectable(term));
        }

        [Fact]
        public void Classify_PreservesTermIndices()
        {
            var terms = _classifier.Classify("foo bar");

            Assert.Equal(2, terms.Count);
            Assert.Equal(0, terms[0].Index);
            Assert.Equal(4, terms[1].Index);
            Assert.Equal("foo", terms[0].Text);
            Assert.Equal("bar", terms[1].Text);
        }

        [Fact]
        public void Classify_EmptyQuery_ReturnsNoTerms()
        {
            Assert.Empty(_classifier.Classify(""));
            Assert.Empty(_classifier.Classify("   "));
        }
    }
}
