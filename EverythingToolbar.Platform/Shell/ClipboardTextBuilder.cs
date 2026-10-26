using System;
using System.Collections.Generic;
using EverythingToolbar.Core.Data;

namespace EverythingToolbar.Platform.Shell
{
    /// <summary>
    /// Builds the clipboard text for the Everything-parity text-copy actions: file names, folder
    /// paths and full name + path, newline-joined in selection order. Pure — no clipboard access.
    /// </summary>
    public static class ClipboardTextBuilder
    {
        /// <summary>Copy Name: the file name of each selected result, one per line.</summary>
        public static string BuildNameLines(IEnumerable<SearchResult?>? selection) =>
            BuildLines(selection, result => result.FileName);

        /// <summary>Copy Path: the containing folder path of each selected result, one per line.</summary>
        public static string BuildPathLines(IEnumerable<SearchResult?>? selection) =>
            BuildLines(selection, result => result.Path);

        /// <summary>Copy Full Name &amp; Path: the full path of each selected result, one per line.</summary>
        public static string BuildFullPathLines(IEnumerable<SearchResult?>? selection) =>
            BuildLines(selection, result => result.FullPathAndFileName);

        private static string BuildLines(IEnumerable<SearchResult?>? selection, Func<SearchResult, string> pick)
        {
            if (selection == null)
                return string.Empty;

            var lines = new List<string>();
            foreach (var result in selection)
            {
                if (result == null)
                    continue;

                var line = pick(result);
                if (!string.IsNullOrWhiteSpace(line))
                    lines.Add(line);
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
