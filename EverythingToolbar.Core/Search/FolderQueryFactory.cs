using System.Text.RegularExpressions;
using EverythingToolbar.Core.Data;

namespace EverythingToolbar.Core.Search
{
    /// <summary>
    /// Builds the Everything queries used by the folder sidebar. Pure and testable: no client, no
    /// I/O. Trailing-separator rule: a trailing separator is stripped before quoting, EXCEPT for a
    /// drive root — `parent:"C:\"` is valid Everything syntax for a drive root (so "C:\Users\"
    /// becomes `parent:"C:\Users"` while "C:\" stays `parent:"C:\"`). UNC paths are never quoted
    /// with a trailing separator ("\\server\share\" becomes "\\server\share").
    /// </summary>
    public static partial class FolderQueryFactory
    {
        /// <summary>Sort used for sidebar children: folder name ascending.</summary>
        public const int SidebarPageSize = 500;

        private static readonly Regex BareDriveRegex = new("^[A-Za-z]:$", RegexOptions.Compiled);

        private static readonly Regex DriveRootRegex = new("^[A-Za-z]:[\\\\/]$", RegexOptions.Compiled);

        /// <summary>
        /// Normalizes a folder path for embedding inside a quoted Everything term. Bare drive
        /// letters ("C:") get their root backslash back ("C:\"), trailing separators are stripped
        /// except on drive roots ("C:\" stays "C:\"), and UNC paths lose any trailing separator.
        /// </summary>
        public static string NormalizeForQuery(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return "";

            var path = folderPath.Trim();

            // "C:" means "the current directory of drive C" in Win32; the sidebar always means the
            // root, so restore the trailing backslash before the root rule below keeps it.
            if (BareDriveRegex.IsMatch(path))
                return path + "\\";

            if (!DriveRootRegex.IsMatch(path))
                path = path.TrimEnd('\\', '/');

            return path;
        }

        /// <summary>Query listing the immediate child folders of the given path.</summary>
        public static SearchQuery BuildChildrenQuery(string folderPath)
        {
            var path = NormalizeForQuery(folderPath);
            return new SearchQuery(
                "parent:\"" + path + "\" folder:",
                SortBy.Name,
                SortDescending: false,
                MatchCase: false,
                MatchPath: false,
                MatchWholeWord: false,
                UseRegex: false
            );
        }

        /// <summary>
        /// Query for navigating the main results to the contents of the given folder. Deliberately
        /// WITHOUT the folder: suffix — files inside the folder must be shown too.
        /// </summary>
        public static SearchQuery BuildNavigateQuery(string folderPath)
        {
            var path = NormalizeForQuery(folderPath);
            return new SearchQuery(
                "parent:\"" + path + "\"",
                SortBy.Name,
                SortDescending: false,
                MatchCase: false,
                MatchPath: false,
                MatchWholeWord: false,
                UseRegex: false
            );
        }
    }
}
