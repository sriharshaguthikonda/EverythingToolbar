using System;
using System.Collections.Generic;

namespace EverythingToolbar.App.Search
{
    /// <summary>
    /// Pure data for one named session: a name plus a saved tab set (deep copies) with the active
    /// index at save time. Deep-copy discipline: the constructor copies every source tab into fresh
    /// <see cref="SearchTab"/> instances, so later mutations of the live tabs (or of the tab
    /// manager's collection) never leak into the stored session; <see cref="CreateRestoreTabs"/>
    /// copies again, so restoring a session never aliases the snapshot's tabs either (restoring
    /// twice yields independent tab objects).
    /// </summary>
    public sealed class SearchSessionSnapshot
    {
        public string Name { get; }

        public IReadOnlyList<SearchTab> Tabs { get; }

        public int ActiveIndex { get; }

        public SearchSessionSnapshot(string name, IEnumerable<SearchTab> tabs, int activeIndex)
        {
            var copied = new List<SearchTab>();
            foreach (var tab in tabs ?? Array.Empty<SearchTab>())
            {
                var copy = new SearchTab();
                (tab ?? new SearchTab()).CopyInto(copy);
                copied.Add(copy);
            }

            Name = (name ?? "").Trim();
            Tabs = copied;
            ActiveIndex = activeIndex;
        }

        /// <summary>Fresh deep copies of the stored tabs, safe to hand to a live tab manager.</summary>
        public List<SearchTab> CreateRestoreTabs()
        {
            var copies = new List<SearchTab>(Tabs.Count);
            foreach (var tab in Tabs)
            {
                var copy = new SearchTab();
                tab.CopyInto(copy);
                copies.Add(copy);
            }
            return copies;
        }
    }
}
