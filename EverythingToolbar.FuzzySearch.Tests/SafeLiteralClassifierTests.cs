using EverythingToolbar.FuzzySearch.Tests.Support;
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
        [InlineData("<foo|bar>")]
        [InlineData("regex:foo.*bar")]
        [InlineData("path:\"C:\\Tools\"")]
        [InlineData("*.pdf")]
        [InlineData("\"C:\\Program Files\"")]
        [InlineData("clinical attachement ext:pdf")]
        [InlineData("neurosicence")]
        public void Classify_Stub_MarksEverythingUnsafeForNow(string query)
        {
            var terms = _classifier.Classify(query);

            Assert.NotEmpty(terms);
            Assert.All(terms, t => Assert.Equal(SearchTermKind.UnsafeOrStructural, t.Kind));
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
