namespace EverythingToolbar.Core.Data
{
    /// <summary>
    /// Pure Windows file-name validation rules (no IO, no WPF) used by the rename dialog:
    /// non-empty, not "." or "..", none of the reserved characters &lt;&gt;:"/\|?*, and no
    /// trailing space or dot. Unicode names pass through untouched.
    /// </summary>
    public static class WindowsPathValidator
    {
        /// <summary>Characters Windows forbids in a file or folder name.</summary>
        public const string InvalidFileNameChars = "<>:\"/\\|?*";

        /// <summary>Whether the given name is a valid Windows file/folder name.</summary>
        public static bool IsValidFileName(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (name == "." || name == "..")
                return false;

            if (name.IndexOfAny(InvalidFileNameChars.ToCharArray()) >= 0)
                return false;

            if (name.EndsWith(' ') || name.EndsWith('.'))
                return false;

            return true;
        }
    }
}
