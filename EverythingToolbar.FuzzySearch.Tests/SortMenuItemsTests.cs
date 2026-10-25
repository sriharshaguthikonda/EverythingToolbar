using System;
using System.Collections.Generic;
using System.Linq;
using EverythingToolbar.Core.Data;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class SortMenuItemsTests
    {
        [Fact]
        public void ItemsCoverWholeEnum()
        {
            var enumValues = Enum.GetValues<SortBy>();

            Assert.Equal(enumValues.Length, SortMenuItems.Items.Count);
            Assert.Equal(enumValues.ToHashSet(), SortMenuItems.Items.Select(i => i.SortBy).ToHashSet());
        }

        [Fact]
        public void DisplayNamesAreNonEmptyAndUnique()
        {
            var names = SortMenuItems.Items.Select(i => i.DisplayName).ToList();

            Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
            Assert.Equal(names.Count, names.ToHashSet().Count);
        }

        [Fact]
        public void GetDisplayNameReturnsNameForEveryEnumValue()
        {
            // A future SortBy enum addition makes GetDisplayName throw here, forcing the
            // menu list to be updated too.
            Assert.All(
                Enum.GetValues<SortBy>(),
                v => Assert.False(string.IsNullOrEmpty(SortMenuItems.GetDisplayName(v)))
            );
        }

        [Fact]
        public void GetDisplayNameThrowsForUndefinedValue()
        {
            var undefined = (SortBy)9999;

            Assert.Throws<ArgumentOutOfRangeException>(() => SortMenuItems.GetDisplayName(undefined));
        }
    }
}
