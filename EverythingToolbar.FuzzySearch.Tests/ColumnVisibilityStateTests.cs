using EverythingToolbar.Core.Data;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class ColumnVisibilityStateTests
    {
        public class Defaults
        {
            [Fact]
            public void NewStateIsVisibleWithNoSavedWidth()
            {
                var state = new ColumnVisibilityState("Path");

                Assert.True(state.IsVisible);
                Assert.Equal(0, state.SavedWidth);
                Assert.Equal("Path", state.ColumnName);
            }

            [Fact]
            public void RoundTripThroughHideAndRestorePreservesDefaults()
            {
                var state = new ColumnVisibilityState("Size", savedWidth: 80);

                state.SetVisible(false, 80);
                var restored = state.SetVisible(true, 0);

                Assert.True(state.IsVisible);
                Assert.Equal(80, restored);
                Assert.Equal(80, state.SavedWidth);
            }
        }

        public class Hide
        {
            [Fact]
            public void SavesCurrentWidthAndCollapsesToZero()
            {
                var state = new ColumnVisibilityState("Path", savedWidth: 260);

                var width = state.SetVisible(false, 260);

                Assert.Equal(0, width);
                Assert.False(state.IsVisible);
                Assert.Equal(260, state.SavedWidth);
            }

            [Fact]
            public void AlreadyHiddenIsNoOp()
            {
                var state = new ColumnVisibilityState("Path");
                state.SetVisible(false, 200);

                var width = state.SetVisible(false, 0);

                Assert.Equal(0, width);
                Assert.False(state.IsVisible);
                Assert.Equal(200, state.SavedWidth);
            }
        }

        public class Restore
        {
            [Fact]
            public void ReturnsSavedWidth()
            {
                var state = new ColumnVisibilityState("Path");
                state.SetVisible(false, 260);

                var width = state.SetVisible(true, 0);

                Assert.Equal(260, width);
                Assert.True(state.IsVisible);
            }

            [Fact]
            public void ZeroSavedWidthRestoresToMinWidth()
            {
                // A column hidden while already collapsed to width 0 must restore to a usable
                // minimum width instead of an invisible 0.
                var state = new ColumnVisibilityState("Size");
                state.SetVisible(false, 0);

                var width = state.SetVisible(true, 0);

                Assert.Equal(ColumnVisibilityState.MinRestoredWidth, width);
            }

            [Fact]
            public void TinySavedWidthRestoresToMinWidth()
            {
                var state = new ColumnVisibilityState("Size");
                state.SetVisible(false, 10);

                var width = state.SetVisible(true, 0);

                Assert.Equal(ColumnVisibilityState.MinRestoredWidth, width);
            }

            [Fact]
            public void AlreadyVisibleIsNoOp()
            {
                var state = new ColumnVisibilityState("Path", savedWidth: 260);

                var width = state.SetVisible(true, 260);

                Assert.Equal(260, width);
                Assert.True(state.IsVisible);
            }
        }

        public class Apply
        {
            [Fact]
            public void HiddenStateYieldsZero()
            {
                var state = new ColumnVisibilityState("Path");
                state.SetVisible(false, 260);

                Assert.Equal(0, ColumnVisibilityState.Apply(state));
            }

            [Fact]
            public void VisibleStateYieldsSavedWidthClampedToMin()
            {
                var hidden = new ColumnVisibilityState("Path");
                hidden.SetVisible(false, 260);
                hidden.SetVisible(true, 0);

                Assert.Equal(260, ColumnVisibilityState.Apply(hidden));

                var collapsed = new ColumnVisibilityState("Size");
                collapsed.SetVisible(false, 0);
                collapsed.SetVisible(true, 0);

                Assert.Equal(ColumnVisibilityState.MinRestoredWidth, ColumnVisibilityState.Apply(collapsed));
            }
        }

        public class Toggle
        {
            [Fact]
            public void ToggleCyclesHideThenRestore()
            {
                var state = new ColumnVisibilityState("Ext", savedWidth: 70);

                Assert.Equal(0, state.Toggle(70));
                Assert.Equal(70, state.Toggle(0));
            }
        }
    }
}
