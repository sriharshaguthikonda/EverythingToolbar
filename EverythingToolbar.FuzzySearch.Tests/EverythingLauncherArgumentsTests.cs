using System.Collections.Generic;
using EverythingToolbar.App.Search;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class EverythingLauncherArgumentsTests
    {
        [Fact]
        public void BuildRenameArguments_QuotesEachPath()
        {
            var paths = new List<string> { @"C:\a\b.txt", @"C:\c d\e.png" };

            var args = EverythingLauncherArguments.BuildRenameArguments(paths);

            Assert.Equal(new[] { "\"C:\\a\\b.txt\"", "\"C:\\c d\\e.png\"" }, args);
        }

        [Fact]
        public void BuildRenameArguments_EmptyList_YieldsNoArguments()
        {
            Assert.Empty(EverythingLauncherArguments.BuildRenameArguments(new List<string>()));
        }

        [Fact]
        public void BuildRenameArguments_EscapesEmbeddedQuotes()
        {
            var args = EverythingLauncherArguments.BuildRenameArguments(new List<string> { @"C:\a""b.txt" });

            Assert.Equal(new[] { "\"C:\\a\"\"b.txt\"" }, args);
        }

        [Fact]
        public void DuplicateNamesSearchTerm_IsBareDupeFunction()
        {
            // The Tools > Find Duplicate Names menu item sets the tab query to exactly this.
            Assert.Equal("dupe:", EverythingLauncherArguments.DuplicateNamesSearchTerm);
        }
    }
}
