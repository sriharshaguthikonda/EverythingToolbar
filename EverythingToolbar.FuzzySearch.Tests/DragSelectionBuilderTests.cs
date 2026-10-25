using System.Runtime.InteropServices.ComTypes;
using EverythingToolbar.Core.Data;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class DragSelectionBuilderTests
    {
        public class BuildFilePaths
        {
            private static SearchResult Result(string path) => new("", "", path, IsFile: true, 0, new FILETIME());

            [Fact]
            public void PreservesOrderAndSkipsBlankPaths()
            {
                SearchResult?[] results = [Result(@"C:\a.txt"), null, Result(""), Result(@"C:\b.txt"), Result("  ")];

                var paths = DragSelectionBuilder.BuildFilePaths(results);

                Assert.Equal([@"C:\a.txt", @"C:\b.txt"], paths);
            }

            [Fact]
            public void NullInputYieldsEmptyList()
            {
                Assert.Empty(DragSelectionBuilder.BuildFilePaths(null));
            }
        }

        public class ShouldStartDrag
        {
            [Fact]
            public void SamePointIsNotADrag()
            {
                Assert.False(DragSelectionBuilder.ShouldStartDrag(10, 10, 10, 10, 4, 4));
            }

            [Fact]
            public void ExactlyAtThresholdIsNotADrag()
            {
                Assert.False(DragSelectionBuilder.ShouldStartDrag(10, 10, 14, 10, 4, 4));
                Assert.False(DragSelectionBuilder.ShouldStartDrag(10, 10, 10, 14, 4, 4));
            }

            [Fact]
            public void BeyondThresholdIsADrag()
            {
                Assert.True(DragSelectionBuilder.ShouldStartDrag(10, 10, 14.5, 10, 4, 4));
                Assert.True(DragSelectionBuilder.ShouldStartDrag(10, 10, 10, 14.5, 4, 4));
            }
        }
    }
}
