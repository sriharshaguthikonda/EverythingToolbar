using EverythingToolbar.App.Search;
using EverythingToolbar.Core.Data;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Pure bookmark-bar logic: the empty-bar visibility rule (enabled AND non-empty) and the
    /// button tooltip text builder (query text, plus "[filter]" when a filter is set).
    /// </summary>
    public sealed class BookmarkBarInfoTests
    {
        [Theory]
        [InlineData(false, 0, false)]
        [InlineData(false, 3, false)]
        [InlineData(true, 0, false)]
        [InlineData(true, 1, true)]
        [InlineData(true, 12, true)]
        public void IsBarVisible_RequiresEnabledAndNonEmpty(bool enabled, int count, bool expected)
        {
            Assert.Equal(expected, BookmarkBarInfo.IsBarVisible(enabled, count));
        }

        [Fact]
        public void BuildTooltip_WithoutFilter_ReturnsQueryTextOnly()
        {
            var bookmark = new SearchBookmark("Docs", "file:*.doc", null, SortBy.Name, false);

            Assert.Equal("file:*.doc", BookmarkBarInfo.BuildTooltip(bookmark));
        }

        [Fact]
        public void BuildTooltip_WithFilter_AppendsBracketedFilterName()
        {
            var bookmark = new SearchBookmark("Docs", "file:*.doc", "Documents", SortBy.Name, false);

            Assert.Equal("file:*.doc  [Documents]", BookmarkBarInfo.BuildTooltip(bookmark));
        }

        [Fact]
        public void BuildTooltip_EmptyFilter_TreatedAsNoFilter()
        {
            var bookmark = new SearchBookmark("Docs", "abc", "", SortBy.Name, false);

            Assert.Equal("abc", BookmarkBarInfo.BuildTooltip(bookmark));
        }

        [Fact]
        public void BuildTooltip_NullBookmark_ReturnsEmpty()
        {
            Assert.Equal("", BookmarkBarInfo.BuildTooltip(null));
        }
    }
}
