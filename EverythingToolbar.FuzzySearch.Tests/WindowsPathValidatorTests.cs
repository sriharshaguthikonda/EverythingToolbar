using System;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Platform.Shell;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class WindowsPathValidatorTests
    {
        public class IsValidFileName
        {
            [Theory]
            [InlineData("report.txt")]
            [InlineData("no_extension")]
            [InlineData("a.b.c")]
            [InlineData("UPPER_lower 123")]
            [InlineData("café ☃.md")]
            [InlineData("日本語.txt")]
            public void AcceptsNormalAndUnicodeNames(string name)
            {
                Assert.True(WindowsPathValidator.IsValidFileName(name));
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            public void RejectsEmpty(string? name)
            {
                Assert.False(WindowsPathValidator.IsValidFileName(name));
            }

            [Theory]
            [InlineData("<")]
            [InlineData(">")]
            [InlineData(":")]
            [InlineData("\"")]
            [InlineData("/")]
            [InlineData("\\")]
            [InlineData("|")]
            [InlineData("?")]
            [InlineData("*")]
            public void RejectsEachInvalidCharacter(string name)
            {
                Assert.False(WindowsPathValidator.IsValidFileName(name));
            }

            [Fact]
            public void RejectsInvalidCharacterAnywhere()
            {
                Assert.False(WindowsPathValidator.IsValidFileName("bad<name.txt"));
                Assert.False(WindowsPathValidator.IsValidFileName("bad|name.txt"));
                Assert.False(WindowsPathValidator.IsValidFileName("bad:name?.txt"));
            }

            [Fact]
            public void RejectsDotAndDotDot()
            {
                Assert.False(WindowsPathValidator.IsValidFileName("."));
                Assert.False(WindowsPathValidator.IsValidFileName(".."));
            }

            [Fact]
            public void RejectsTrailingSpaceOrDot()
            {
                Assert.False(WindowsPathValidator.IsValidFileName("name "));
                Assert.False(WindowsPathValidator.IsValidFileName("name."));
                Assert.False(WindowsPathValidator.IsValidFileName("name.txt "));
            }

            [Fact]
            public void AcceptsLeadingAndEmbeddedDotsAndSpaces()
            {
                Assert.True(WindowsPathValidator.IsValidFileName(".gitignore"));
                Assert.True(WindowsPathValidator.IsValidFileName("my file.v2.txt"));
            }
        }

        public class PlanRename
        {
            private static SearchResult Result(string path) =>
                new("", "", path, IsFile: true, 0, new System.Runtime.InteropServices.ComTypes.FILETIME());

            [Fact]
            public void NullSelectionIsDisabled()
            {
                Assert.Null(EditOperationPlanner.PlanRename(null).FullPath);
            }

            [Fact]
            public void EmptySelectionIsDisabled()
            {
                Assert.Null(EditOperationPlanner.PlanRename(Array.Empty<SearchResult?>()).FullPath);
            }

            [Fact]
            public void MultipleSelectionIsDisabled()
            {
                var plan = EditOperationPlanner.PlanRename(
                    new SearchResult?[] { Result(@"C:\a.txt"), Result(@"C:\b.txt") }
                );
                Assert.Null(plan.FullPath);
                Assert.Null(plan.FileName);
            }

            [Fact]
            public void SingleSelectionReturnsPathAndName()
            {
                var plan = EditOperationPlanner.PlanRename(new SearchResult?[] { Result(@"C:\dir\a.txt") });
                Assert.Equal(@"C:\dir\a.txt", plan.FullPath);
                Assert.Equal("a.txt", plan.FileName);
            }

            [Fact]
            public void NullOrEmptyEntriesAreDisabled()
            {
                Assert.Null(EditOperationPlanner.PlanRename(new SearchResult?[] { null }).FullPath);
                Assert.Null(
                    EditOperationPlanner.PlanRename(new SearchResult?[] { Result(""), Result(@"C:\b.txt") }).FullPath
                );
            }

            [Fact]
            public void F2WithNoModifiersMapsToRename()
            {
                Assert.Equal(
                    EditFileAction.Rename,
                    EditOperationPlanner.MapGesture(System.Windows.Input.Key.F2, System.Windows.Input.ModifierKeys.None)
                );
            }

            [Fact]
            public void ModifiedF2MapsToNone()
            {
                Assert.Equal(
                    EditFileAction.None,
                    EditOperationPlanner.MapGesture(
                        System.Windows.Input.Key.F2,
                        System.Windows.Input.ModifierKeys.Control
                    )
                );
            }
        }
    }
}
