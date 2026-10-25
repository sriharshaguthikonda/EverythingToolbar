using System;
using System.Runtime.InteropServices.ComTypes;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Platform.Shell;
using EverythingToolbar.Search;
using Xunit;
using DropEffectCodec = EverythingToolbar.Platform.Shell.PreferredDropEffectCodec;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class EditOperationPlannerTests
    {
        private static SearchResult Result(string path, bool isFile) =>
            new("", "", path, IsFile: isFile, 0, new FILETIME());

        public class CanActOnSelection
        {
            [Fact]
            public void NullSelectionIsDisabled()
            {
                Assert.False(EditOperationPlanner.CanActOnSelection(null));
            }

            [Fact]
            public void EmptySelectionIsDisabled()
            {
                Assert.False(EditOperationPlanner.CanActOnSelection(Array.Empty<SearchResult?>()));
            }

            [Fact]
            public void NullAndBlankEntriesDoNotEnable()
            {
                Assert.False(EditOperationPlanner.CanActOnSelection(new SearchResult?[] { null, Result("", true) }));
            }

            [Fact]
            public void SingleSelectionIsEnabled()
            {
                Assert.True(EditOperationPlanner.CanActOnSelection(new[] { Result(@"C:\a.txt", true) }));
            }
        }

        public class PlanPaste
        {
            [Fact]
            public void NullSelectionIsDisabled()
            {
                Assert.False(EditOperationPlanner.PlanPaste(null, clipboardHasFiles: true).CanPaste);
            }

            [Fact]
            public void EmptyClipboardIsDisabled()
            {
                var plan = EditOperationPlanner.PlanPaste(
                    new[] { Result(@"C:\folder", isFile: false) },
                    clipboardHasFiles: false
                );
                Assert.False(plan.CanPaste);
                Assert.Null(plan.DestinationFolder);
            }

            [Fact]
            public void NoSelectionIsDisabled()
            {
                Assert.False(
                    EditOperationPlanner.PlanPaste(Array.Empty<SearchResult?>(), clipboardHasFiles: true).CanPaste
                );
            }

            [Fact]
            public void SingleSelectedFileIsDisabled()
            {
                Assert.False(
                    EditOperationPlanner
                        .PlanPaste(new[] { Result(@"C:\a.txt", isFile: true) }, clipboardHasFiles: true)
                        .CanPaste
                );
            }

            [Fact]
            public void MultipleSelectedFoldersAreDisabled()
            {
                Assert.False(
                    EditOperationPlanner
                        .PlanPaste(
                            new[] { Result(@"C:\folder1", isFile: false), Result(@"C:\folder2", isFile: false) },
                            clipboardHasFiles: true
                        )
                        .CanPaste
                );
            }

            [Fact]
            public void SingleSelectedFolderIsTheDestination()
            {
                var plan = EditOperationPlanner.PlanPaste(
                    new[] { Result(@"C:\folder", isFile: false) },
                    clipboardHasFiles: true
                );
                Assert.True(plan.CanPaste);
                Assert.Equal(@"C:\folder", plan.DestinationFolder);
            }

            [Fact]
            public void BlankPathResultDisablesPaste()
            {
                Assert.False(
                    EditOperationPlanner
                        .PlanPaste(new[] { Result("", isFile: false) }, clipboardHasFiles: true)
                        .CanPaste
                );
            }
        }

        public class MapGesture
        {
            [Theory]
            [InlineData(EditFileAction.Copy, System.Windows.Input.Key.C, System.Windows.Input.ModifierKeys.Control)]
            [InlineData(EditFileAction.Cut, System.Windows.Input.Key.X, System.Windows.Input.ModifierKeys.Control)]
            [InlineData(EditFileAction.Paste, System.Windows.Input.Key.V, System.Windows.Input.ModifierKeys.Control)]
            [InlineData(EditFileAction.Delete, System.Windows.Input.Key.Delete, System.Windows.Input.ModifierKeys.None)]
            [InlineData(
                EditFileAction.CopyFullPath,
                System.Windows.Input.Key.C,
                System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift
            )]
            [InlineData(
                EditFileAction.CopyName,
                System.Windows.Input.Key.N,
                System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift
            )]
            [InlineData(
                EditFileAction.CopyPath,
                System.Windows.Input.Key.P,
                System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift
            )]
            public void EverythingParityGesturesMapToActions(
                EditFileAction expected,
                System.Windows.Input.Key key,
                System.Windows.Input.ModifierKeys modifiers
            )
            {
                Assert.Equal(expected, EditOperationPlanner.MapGesture(key, modifiers));
            }

            [Theory]
            [InlineData(System.Windows.Input.Key.C, System.Windows.Input.ModifierKeys.None)]
            [InlineData(System.Windows.Input.Key.C, System.Windows.Input.ModifierKeys.Shift)]
            [InlineData(
                System.Windows.Input.Key.C,
                System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt
            )]
            [InlineData(System.Windows.Input.Key.Delete, System.Windows.Input.ModifierKeys.Shift)]
            [InlineData(System.Windows.Input.Key.W, System.Windows.Input.ModifierKeys.Control)]
            [InlineData(
                System.Windows.Input.Key.W,
                System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift
            )]
            [InlineData(System.Windows.Input.Key.I, System.Windows.Input.ModifierKeys.Control)]
            public void NonEditGesturesMapToNone(
                System.Windows.Input.Key key,
                System.Windows.Input.ModifierKeys modifiers
            )
            {
                Assert.Equal(EditFileAction.None, EditOperationPlanner.MapGesture(key, modifiers));
            }
        }

        public class ClipboardTextBuilder
        {
            [Fact]
            public void NullSelectionBuildsEmptyText()
            {
                Assert.Equal("", EverythingToolbar.Platform.Shell.ClipboardTextBuilder.BuildNameLines(null));
                Assert.Equal("", EverythingToolbar.Platform.Shell.ClipboardTextBuilder.BuildPathLines(null));
                Assert.Equal("", EverythingToolbar.Platform.Shell.ClipboardTextBuilder.BuildFullPathLines(null));
            }

            [Fact]
            public void SingleSelectionBuildsOneLine()
            {
                var selection = new[] { Result(@"C:\folder\a.txt", isFile: true) };

                Assert.Equal("a.txt", EverythingToolbar.Platform.Shell.ClipboardTextBuilder.BuildNameLines(selection));
                Assert.Equal(
                    @"C:\folder",
                    EverythingToolbar.Platform.Shell.ClipboardTextBuilder.BuildPathLines(selection)
                );
                Assert.Equal(
                    @"C:\folder\a.txt",
                    EverythingToolbar.Platform.Shell.ClipboardTextBuilder.BuildFullPathLines(selection)
                );
            }

            [Fact]
            public void MultiSelectionPreservesOrderAndJoinsWithNewlines()
            {
                var selection = new[]
                {
                    Result(@"C:\b\second.txt", isFile: true),
                    Result(@"C:\a\first.txt", isFile: true),
                };
                var nl = Environment.NewLine;

                Assert.Equal(
                    "second.txt" + nl + "first.txt",
                    EverythingToolbar.Platform.Shell.ClipboardTextBuilder.BuildNameLines(selection)
                );
                Assert.Equal(
                    @"C:\b" + nl + @"C:\a",
                    EverythingToolbar.Platform.Shell.ClipboardTextBuilder.BuildPathLines(selection)
                );
                Assert.Equal(
                    @"C:\b\second.txt" + nl + @"C:\a\first.txt",
                    EverythingToolbar.Platform.Shell.ClipboardTextBuilder.BuildFullPathLines(selection)
                );
            }

            [Fact]
            public void NullAndBlankEntriesAreSkipped()
            {
                var selection = new SearchResult?[]
                {
                    null,
                    Result(@"C:\keep.txt", isFile: true),
                    Result("", isFile: true),
                    Result("   ", isFile: true),
                };

                Assert.Equal(
                    "keep.txt",
                    EverythingToolbar.Platform.Shell.ClipboardTextBuilder.BuildNameLines(selection)
                );
                Assert.Equal(@"C:\", EverythingToolbar.Platform.Shell.ClipboardTextBuilder.BuildPathLines(selection));
                Assert.Equal(
                    @"C:\keep.txt",
                    EverythingToolbar.Platform.Shell.ClipboardTextBuilder.BuildFullPathLines(selection)
                );
            }
        }

        public class PreferredDropEffectCodec
        {
            [Fact]
            public void CopyEncodesToDropeffectCopy()
            {
                Assert.Equal(1, BitConverter.ToInt32(DropEffectCodec.Encode(isCut: false), 0));
            }

            [Fact]
            public void CutEncodesToDropeffectMove()
            {
                Assert.Equal(2, BitConverter.ToInt32(DropEffectCodec.Encode(isCut: true), 0));
            }

            [Fact]
            public void RoundTrips()
            {
                Assert.False(DropEffectCodec.DecodeIsCut(DropEffectCodec.Encode(isCut: false)));
                Assert.True(DropEffectCodec.DecodeIsCut(DropEffectCodec.Encode(isCut: true)));
            }

            [Fact]
            public void ShortOrMissingPayloadIsNotCut()
            {
                Assert.False(DropEffectCodec.DecodeIsCut(null));
                Assert.False(DropEffectCodec.DecodeIsCut(Array.Empty<byte>()));
                Assert.False(DropEffectCodec.DecodeIsCut(new byte[] { 2 }));
            }

            [Fact]
            public void CopyFlagBitIsNotCut()
            {
                Assert.False(DropEffectCodec.DecodeIsCut(BitConverter.GetBytes(1)));
            }
        }
    }
}
