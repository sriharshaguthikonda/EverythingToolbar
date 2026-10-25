using System;
using System.Collections.Generic;
using System.Linq;
using EverythingToolbar.App;
using EverythingToolbar.Core.Data;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Tests for <see cref="BatchRenameApply"/> — the pure seam between the batch-rename dialog
    /// and <see cref="BatchRenameEngine"/> (summary line + effective change list). No WPF, no I/O.
    /// </summary>
    public class BatchRenameApplyTests
    {
        private static BatchRenamePlan Plan(IEnumerable<string> names, BatchRenameOptions options) =>
            BatchRenameEngine.Plan(names.ToList(), options);

        public class BuildSummary
        {
            [Fact]
            public void FormatsCountsInDocumentedShape()
            {
                Assert.Equal("2 of 5 will be renamed (3 skipped)", BatchRenameApply.BuildSummary(2, 5, 3));
            }

            [Fact]
            public void ZeroCountsAreHandled()
            {
                Assert.Equal("0 of 0 will be renamed (0 skipped)", BatchRenameApply.BuildSummary(0, 0, 0));
            }
        }

        public class SelectEffectiveChanges
        {
            private static readonly BatchRenameOptions Options = new("a", "b", false, false, false, false, null);

            private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(
                StringComparer.Ordinal
            )
            {
                ["cat.txt"] = @"C:\docs\cat.txt",
                ["car.txt"] = @"D:\pictures\car.txt",
                ["same.txt"] = @"C:\docs\same.txt",
                ["bad?.txt"] = @"C:\docs\bad?.txt",
            };

            [Fact]
            public void MapsChangesToFullPathOfTheOriginal()
            {
                var plan = Plan(new[] { "cat.txt", "car.txt", "same.txt" }, Options);
                var changes = BatchRenameApply.SelectEffectiveChanges(plan, Map);

                Assert.Equal(
                    new[]
                    {
                        (@"C:\docs\cbt.txt", "cbt.txt"),
                        (@"D:\pictures\cbr.txt", "cbr.txt"),
                        (@"C:\docs\sbme.txt", "sbme.txt"),
                    },
                    changes
                );
            }

            [Fact]
            public void SkipsNoChangeItems()
            {
                // "xyz.txt" contains no "a", so the plan leaves it unchanged and it is skipped.
                var plan = Plan(new[] { "xyz.txt" }, Options);
                var changes = BatchRenameApply.SelectEffectiveChanges(plan, Map);

                Assert.Empty(changes);
            }

            [Fact]
            public void SkipsErroredItems()
            {
                // "bad?.txt" -> "bbd?.txt" still contains '?', which Windows forbids, so the
                // engine flags the item with an error and the change list must skip it.
                var plan = Plan(new[] { "bad?.txt" }, Options);

                Assert.NotNull(plan.Items[0].Error);

                var changes = BatchRenameApply.SelectEffectiveChanges(plan, Map);
                Assert.Empty(changes);
            }

            [Fact]
            public void SkipsNamesMissingFromTheMap()
            {
                var plan = Plan(new[] { "cat.txt", "ghost.txt" }, Options);
                var changes = BatchRenameApply.SelectEffectiveChanges(plan, Map);

                var change = Assert.Single(changes);
                Assert.Equal(@"C:\docs\cbt.txt", change.Path);
            }

            [Fact]
            public void PreservesPlanOrder()
            {
                var plan = Plan(new[] { "car.txt", "cat.txt" }, Options);
                var changes = BatchRenameApply.SelectEffectiveChanges(plan, Map);

                Assert.Equal(
                    new[] { @"D:\pictures\cbr.txt", @"C:\docs\cbt.txt" },
                    changes.Select(change => change.Path).ToArray()
                );
            }
        }
    }
}
