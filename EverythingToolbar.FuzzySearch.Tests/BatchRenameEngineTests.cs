using System;
using System.Linq;
using EverythingToolbar.Core.Data;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class BatchRenameEngineTests
    {
        private static BatchRenameItem PlanOne(string name, BatchRenameOptions options)
        {
            return BatchRenameEngine.Plan(new[] { name }, options).Items[0];
        }

        public class LiteralReplace
        {
            [Fact]
            public void ReplacesFirstOccurrenceByDefault()
            {
                var options = new BatchRenameOptions("a", "b", false, false, false, false, null);
                var item = PlanOne("banana.txt", options);
                Assert.Equal("bbnana.txt", item.New);
                Assert.Null(item.Error);
            }

            [Fact]
            public void ReplacesAllOccurrencesWhenMatchAll()
            {
                var options = new BatchRenameOptions("a", "b", false, false, true, false, null);
                var item = PlanOne("banana.txt", options);
                Assert.Equal("bbnbnb.txt", item.New);
            }

            [Fact]
            public void CaseInsensitiveByDefault()
            {
                var options = new BatchRenameOptions("BAN", "x", false, false, false, false, null);
                var item = PlanOne("banana.txt", options);
                Assert.Equal("xana.txt", item.New);
            }

            [Fact]
            public void CaseSensitiveWhenRequested()
            {
                var options = new BatchRenameOptions("BAN", "x", false, true, false, false, null);
                var item = PlanOne("banana.txt", options);
                Assert.Equal("banana.txt", item.New);
            }

            [Fact]
            public void ExtensionPreservedByDefault()
            {
                var options = new BatchRenameOptions("port", "draft", false, false, false, false, null);
                var item = PlanOne("report.docx", options);
                Assert.Equal("redraft.docx", item.New);
            }

            [Fact]
            public void IncludeExtensionRenamesFullName()
            {
                var options = new BatchRenameOptions("docx", "txt", false, false, false, true, null);
                var item = PlanOne("report.docx", options);
                Assert.Equal("report.txt", item.New);
            }

            [Fact]
            public void IncludeExtensionOffDoesNotTouchExtension()
            {
                var options = new BatchRenameOptions("report", "x", false, false, false, true, null);
                var item = PlanOne("report.docx", options);
                Assert.Equal("x.docx", item.New);
            }

            [Fact]
            public void HandlesSpacesAndUnicode()
            {
                var options = new BatchRenameOptions(" ", "_", false, false, true, false, null);
                var item = PlanOne("mein \u00e4ltes Bild.png", options);
                Assert.Equal("mein_\u00e4ltes_Bild.png", item.New);
            }

            [Fact]
            public void UnmatchedNamesComeBackUnchanged()
            {
                var options = new BatchRenameOptions("zzz", "x", false, false, false, false, null);
                var item = PlanOne("photo.jpg", options);
                Assert.Equal("photo.jpg", item.New);
                Assert.Null(item.Error);
            }
        }

        public class RegexReplace
        {
            [Fact]
            public void GroupSubstitution()
            {
                var options = new BatchRenameOptions("(\\d+)_(\\w+)", "$2_$1", true, false, false, false, null);
                var item = PlanOne("2024_report.docx", options);
                Assert.Equal("report_2024.docx", item.New);
            }

            [Fact]
            public void ReplacesAllMatchesInRegexMode()
            {
                var options = new BatchRenameOptions("o", "0", true, false, false, false, null);
                var item = PlanOne("photo.jpg", options);
                Assert.Equal("ph0t0.jpg", item.New);
            }

            [Fact]
            public void AnchoredPattern()
            {
                var options = new BatchRenameOptions("^IMG_", "photo_", true, false, false, false, null);
                var item = PlanOne("IMG_1234.jpg", options);
                Assert.Equal("photo_1234.jpg", item.New);
            }

            [Fact]
            public void AnchoredPatternAppliesToStemEnd()
            {
                var options = new BatchRenameOptions("_suffix$", "", true, false, false, false, null);
                var item = PlanOne("file_suffix.jpg", options);
                Assert.Equal("file.jpg", item.New);
            }

            [Fact]
            public void CaseInsensitiveByDefault()
            {
                var options = new BatchRenameOptions("[a-z]+", "x", true, false, false, false, null);
                var item = PlanOne("ABC.jpg", options);
                Assert.Equal("x.jpg", item.New);
            }

            [Fact]
            public void CaseSensitiveWhenRequested()
            {
                var options = new BatchRenameOptions("[a-z]+", "x", true, true, false, false, null);
                var item = PlanOne("ABC.jpg", options);
                Assert.Equal("ABC.jpg", item.New);
            }

            [Fact]
            public void InvalidRegexSurfacesErrorOnce()
            {
                var options = new BatchRenameOptions("([unclosed", "x", true, false, false, false, null);
                var plan = BatchRenameEngine.Plan(new[] { "a.txt", "b.txt", "c.txt" }, options);
                Assert.Equal(BatchRenameEngine.InvalidRegexError, plan.Error);
                Assert.True(plan.HasError);
                Assert.All(plan.Items, i => Assert.Null(i.Error));
                Assert.All(plan.Items, i => Assert.Equal(i.Original, i.New));
            }

            [Fact]
            public void ExplicitCaptureSemantics_UnnamedGroupsNotCaptured()
            {
                // .NET regex: only parenthesized groups capture unless (?:...) is used.
                var options = new BatchRenameOptions("(\\w+)-(?:\\d+)", "$1", true, false, false, false, null);
                var item = PlanOne("file-42.txt", options);
                Assert.Equal("file.txt", item.New);
            }
        }

        public class Counters
        {
            [Fact]
            public void SequenceAcrossFiveFilesWithPadding()
            {
                var options = new BatchRenameOptions(
                    "^",
                    "img_${start=10,increment=5,padding=3}_",
                    true,
                    false,
                    false,
                    false,
                    "on"
                );
                var plan = BatchRenameEngine.Plan(new[] { "a.jpg", "b.jpg", "c.jpg", "d.jpg", "e.jpg" }, options);
                Assert.Equal(
                    new[] { "img_010_a.jpg", "img_015_b.jpg", "img_020_c.jpg", "img_025_d.jpg", "img_030_e.jpg" },
                    plan.Items.Select(i => i.New).ToArray()
                );
                Assert.All(plan.Items, i => Assert.Null(i.Error));
            }

            [Fact]
            public void DefaultsAreStartZeroIncrementOnePaddingZero()
            {
                // Defaults verified against PowerToys src/modules/powerrename/lib/Enumerating.h:
                // start.value_or(0), increment.value_or(1), padding.value_or(0).
                var options = new BatchRenameOptions("^", "${}_", true, false, false, false, "on");
                var plan = BatchRenameEngine.Plan(new[] { "a.txt", "b.txt", "c.txt" }, options);
                Assert.Equal(new[] { "0_a.txt", "1_b.txt", "2_c.txt" }, plan.Items.Select(i => i.New).ToArray());
            }

            [Fact]
            public void CounterCombinedWithLiteralText()
            {
                var options = new BatchRenameOptions("^.*$", "photo_${padding=2}.jpg", true, false, false, false, "on");
                var plan = BatchRenameEngine.Plan(new[] { "x", "y" }, options);
                Assert.Equal(new[] { "photo_00.jpg", "photo_01.jpg" }, plan.Items.Select(i => i.New).ToArray());
            }

            [Fact]
            public void MultipleCountersGetSamePerItemValueWithOwnSettings()
            {
                var options = new BatchRenameOptions(
                    "^",
                    "${start=1,increment=1}_${start=100,increment=10,padding=4}_",
                    true,
                    false,
                    false,
                    false,
                    "on"
                );
                var plan = BatchRenameEngine.Plan(new[] { "a.txt", "b.txt" }, options);
                Assert.Equal(new[] { "1_0100_a.txt", "2_0110_b.txt" }, plan.Items.Select(i => i.New).ToArray());
            }

            [Fact]
            public void CounterDoesNotAdvanceWithinOneName()
            {
                // Two matches inside one name: literal replace-all with a counter pattern
                // must insert the SAME value for both occurrences.
                var options = new BatchRenameOptions(
                    "-",
                    "_${start=0,increment=1,padding=0}_",
                    false,
                    false,
                    true,
                    false,
                    "on"
                );
                var item = PlanOne("a-b-c.txt", options);
                Assert.Equal("a_0_b_0_c.txt", item.New);
            }

            [Fact]
            public void CounterAdvancesOnlyForMatchingItems()
            {
                // PowerRename increments the shared index only when a match occurred.
                var options = new BatchRenameOptions("x", "${start=1,increment=1}", true, false, false, false, "on");
                var plan = BatchRenameEngine.Plan(new[] { "ax", "bbb", "cx", "dx" }, options);
                Assert.Equal(new[] { "a1", "bbb", "c2", "d3" }, plan.Items.Select(i => i.New).ToArray());
            }

            [Fact]
            public void LiteralCounterPatternWithoutToggleStaysLiteral()
            {
                var options = new BatchRenameOptions("-", "_${start=1}_", false, false, true, false, null);
                var item = PlanOne("a-b.txt", options);
                Assert.Equal("a_${start=1}_b.txt", item.New);
            }

            [Fact]
            public void CounterWithNoSearchRewritesWholeTarget()
            {
                var options = new BatchRenameOptions(
                    "",
                    "file_${start=3,increment=2,padding=2}.txt",
                    false,
                    false,
                    false,
                    true,
                    "on"
                );
                var plan = BatchRenameEngine.Plan(new[] { "old1.dat", "old2.dat" }, options);
                Assert.Equal(new[] { "file_03.txt", "file_05.txt" }, plan.Items.Select(i => i.New).ToArray());
            }
        }

        public class Validation
        {
            [Fact]
            public void EmptyNewNameGetsError()
            {
                var options = new BatchRenameOptions(".*", "", true, false, false, true, null);
                var item = PlanOne("file.txt", options);
                Assert.Equal("", item.New);
                Assert.Equal(BatchRenameEngine.EmptyNameError, item.Error);
            }

            [Fact]
            public void InvalidCharactersGetError()
            {
                var options = new BatchRenameOptions(" ", ":", false, false, false, false, null);
                var item = PlanOne("a b.txt", options);
                Assert.Equal("a:b.txt", item.New);
                Assert.Equal(BatchRenameEngine.InvalidNameError, item.Error);
            }

            [Fact]
            public void UnchangedNamesDoNotGetErrors()
            {
                // Names the search never touches pass through untouched, even names
                // that merely contain characters that LOOK odd but are legal here.
                var options = new BatchRenameOptions("zzz", "y", false, false, false, false, null);
                var plan = BatchRenameEngine.Plan(new[] { "a#bad.txt", "ok.txt" }, options);
                Assert.All(plan.Items, i => Assert.Null(i.Error));
                Assert.Equal("a#bad.txt", plan.Items[0].New);
            }

            [Fact]
            public void NoOpWhenSearchEmptyAndNoCounter()
            {
                var options = new BatchRenameOptions("", "anything", false, false, false, false, null);
                var plan = BatchRenameEngine.Plan(new[] { "a.txt", "b.txt" }, options);
                Assert.True(plan.IsNoOp);
                Assert.Equal(new[] { "a.txt", "b.txt" }, plan.Items.Select(i => i.New).ToArray());
                Assert.All(plan.Items, i => Assert.Null(i.Error));
            }

            [Fact]
            public void NotNoOpWhenCounterPresentInReplace()
            {
                var options = new BatchRenameOptions("", "${}.txt", false, false, false, false, "on");
                var plan = BatchRenameEngine.Plan(new[] { "a", "b" }, options);
                Assert.False(plan.IsNoOp);
                Assert.Equal(new[] { "0.txt", "1.txt" }, plan.Items.Select(i => i.New).ToArray());
            }

            [Fact]
            public void EmptyFileListGivesEmptyNoOpPlan()
            {
                var options = new BatchRenameOptions("a", "b", false, false, false, false, null);
                var plan = BatchRenameEngine.Plan(Array.Empty<string>(), options);
                Assert.Empty(plan.Items);
                Assert.False(plan.HasError);
            }

            [Fact]
            public void ReservedNamesDotAndDotDotGetError()
            {
                var options = new BatchRenameOptions("x", "", true, false, false, true, null);
                var item = PlanOne("..x", options);
                Assert.Equal("..", item.New);
                Assert.Equal(BatchRenameEngine.InvalidNameError, item.Error);
            }
        }

        public class ExtensionInteraction
        {
            [Fact]
            public void SearchAppliesToStemOnlyAndExtensionReattached()
            {
                // report.docx with search "port" -> stem "report" becomes "redraft",
                // the extension is re-attached verbatim.
                var options = new BatchRenameOptions("port", "draft", false, false, false, false, null);
                var item = PlanOne("report.docx", options);
                Assert.Equal("redraft.docx", item.New);
            }

            [Fact]
            public void SearchMatchingExtensionDoesNotMatchWhenIncludeExtensionOff()
            {
                var options = new BatchRenameOptions("docx", "txt", false, false, false, false, null);
                var item = PlanOne("report.docx", options);
                Assert.Equal("report.docx", item.New);
            }

            [Fact]
            public void DotfileStemIncludesTheDot()
            {
                var options = new BatchRenameOptions("git", "hub", false, false, false, false, null);
                var item = PlanOne(".gitignore", options);
                Assert.Equal(".hubignore", item.New);
            }

            [Fact]
            public void ExtensionlessFileRenamedWhole()
            {
                var options = new BatchRenameOptions("draft", "final", false, false, false, false, null);
                var item = PlanOne("draft", options);
                Assert.Equal("final", item.New);
            }

            [Fact]
            public void RegexWithExtensionToggleMatchesWholeName()
            {
                var options = new BatchRenameOptions("\\.(jpg|png)$", ".webp", true, false, false, true, null);
                var item = PlanOne("photo.jpg", options);
                Assert.Equal("photo.webp", item.New);
            }
        }
    }
}
