using System;
using EverythingToolbar.Core.Data;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class SortCycleTests
    {
        public class Next
        {
            [Fact]
            public void UnsortedColumnStartsAscending()
            {
                var result = SortCycle.Next(SortBy.Name, SortBy.Size, descending: false);

                Assert.Equal((SortBy.Size, false), result);
            }

            [Fact]
            public void AscendingColumnTurnsDescending()
            {
                var result = SortCycle.Next(SortBy.Name, SortBy.Name, descending: false);

                Assert.Equal((SortBy.Name, true), result);
            }

            [Fact]
            public void DescendingColumnResetsToDefault()
            {
                var result = SortCycle.Next(SortBy.Name, SortBy.Name, descending: true);

                Assert.Null(result);
            }

            [Fact]
            public void ClickingDifferentColumnWhileDescendingStartsAscending()
            {
                var result = SortCycle.Next(SortBy.DateModified, SortBy.Path, descending: true);

                Assert.Equal((SortBy.Path, false), result);
            }

            [Fact]
            public void EverySortByKeyIsCycleable()
            {
                foreach (SortBy sortBy in Enum.GetValues(typeof(SortBy)))
                {
                    var other = sortBy == SortBy.Name ? SortBy.DateModified : SortBy.Name;
                    var asc = SortCycle.Next(other, sortBy, descending: true);
                    Assert.Equal((sortBy, false), asc);

                    var desc = SortCycle.Next(sortBy, sortBy, descending: false);
                    Assert.Equal((sortBy, true), desc);

                    var reset = SortCycle.Next(sortBy, sortBy, descending: true);
                    Assert.Null(reset);
                }
            }
        }
    }
}
