using System;
using System.Runtime.InteropServices.ComTypes;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Platform.Shell;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class ShellContextMenuSelectionTests
    {
        public class ShouldReselectOnRightClick
        {
            [Fact]
            public void UnselectedClickedItemIsReselected()
            {
                Assert.True(ShellContextMenuSelection.ShouldReselectOnRightClick(false, 3));
            }

            [Fact]
            public void UnselectedClickedItemWithEmptySelectionIsReselected()
            {
                Assert.True(ShellContextMenuSelection.ShouldReselectOnRightClick(false, 0));
            }

            [Fact]
            public void SelectedClickedItemKeepsMultiSelection()
            {
                Assert.False(ShellContextMenuSelection.ShouldReselectOnRightClick(true, 4));
            }
        }

        public class BuildPathList
        {
            private static SearchResult Result(string path) => new("", "", path, IsFile: true, 0, new FILETIME());

            [Fact]
            public void NullInputYieldsEmptyList()
            {
                Assert.Empty(ShellContextMenuSelection.BuildPathList(null));
            }

            [Fact]
            public void NullAndBlankResultsAreSkipped()
            {
                var paths = ShellContextMenuSelection.BuildPathList(
                    new SearchResult?[] { null, Result(""), Result("   "), Result(@"C:\a.txt") }
                );

                Assert.Equal(new[] { @"C:\a.txt" }, paths);
            }

            [Fact]
            public void OrderIsPreserved()
            {
                var paths = ShellContextMenuSelection.BuildPathList(new[] { Result(@"C:\b.txt"), Result(@"C:\a.txt") });

                Assert.Equal(new[] { @"C:\b.txt", @"C:\a.txt" }, paths);
            }

            [Fact]
            public void DuplicatesAreRemovedCaseInsensitively()
            {
                var paths = ShellContextMenuSelection.BuildPathList(
                    new[] { Result(@"C:\a.txt"), Result(@"C:\A.TXT"), Result(@"C:\a.txt") }
                );

                Assert.Equal(new[] { @"C:\a.txt" }, paths);
            }
        }
    }
}
