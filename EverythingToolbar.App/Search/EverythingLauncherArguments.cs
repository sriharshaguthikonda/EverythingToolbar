using System.Collections.Generic;
using System.Linq;

namespace EverythingToolbar.App.Search
{
    /// <summary>Pure helpers for building Everything.exe command-line arguments.</summary>
    public static class EverythingLauncherArguments
    {
        /// <summary>
        /// Bare <c>dupe:</c> query; Everything's Tools > Find Duplicate Names equivalent
        /// (finds files that share the same name with other files).
        /// </summary>
        public const string DuplicateNamesSearchTerm = "dupe:";

        /// <summary>Builds the quoted path arguments for <c>-rename</c> (one argument per path).</summary>
        public static IReadOnlyList<string> BuildRenameArguments(IReadOnlyList<string> paths)
        {
            return paths.Select(Quote).ToList();
        }

        private static string Quote(string path)
        {
            return "\"" + path.Replace("\"", "\"\"") + "\"";
        }
    }
}
