using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using EverythingToolbar.Core.Data;

namespace EverythingToolbar.Core.Search
{
    /// <summary>Supported export file formats for search results.</summary>
    public enum ExportFormat
    {
        Csv,
        Txt,
        Efu,
    }

    /// <summary>
    /// Pure, testable writer for search-result exports. Exposes whole-enumeration writers (which
    /// stream without materializing the set) plus per-row writers so services can interleave
    /// paging with writing. All text uses the encoding of the caller's TextWriter; the export
    /// service picks UTF-8 with a BOM for CSV/EFU (Excel friendliness — documented choice).
    /// </summary>
    public static class ResultExporter
    {
        /// <summary>Everything's documented EFU header layout.</summary>
        public const string EfuHeader = "Filename,Size,Date Modified,Date Created,Attributes";

        /// <summary>
        /// Quotes a CSV field per RFC 4180: quoted when the value contains a comma, quote, CR or
        /// LF, with embedded quotes doubled.
        /// </summary>
        public static string EscapeCsvField(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var needsQuotes =
                value.IndexOf(',') >= 0
                || value.IndexOf('"') >= 0
                || value.IndexOf('\r') >= 0
                || value.IndexOf('\n') >= 0;
            if (!needsQuotes)
                return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>Plain-text export: one full path per line (Everything's "File list (txt)").</summary>
        public static void WriteTxt(TextWriter writer, IEnumerable<SearchResult> results)
        {
            foreach (var result in results)
                WriteTxtRow(writer, result);
        }

        public static void WriteTxtRow(TextWriter writer, SearchResult result)
        {
            writer.WriteLine(result.FullPathAndFileName);
        }

        /// <summary>
        /// CSV export: header line plus one row per item with the EFU column set in
        /// human-readable form (local, sortable dates). RFC 4180 quoting throughout so
        /// spreadsheets round-trip the metadata columns.
        /// </summary>
        public const string CsvHeader = "Filename,Size,Date Modified,Date Created,Attributes";

        public static void WriteCsv(TextWriter writer, IEnumerable<SearchResult> results)
        {
            writer.WriteLine(CsvHeader);
            foreach (var result in results)
                WriteCsvRow(writer, result);
        }

        public static void WriteCsvRow(TextWriter writer, SearchResult result)
        {
            var dateModified = FileTimeToLocalDateTime(result.DateModified);
            writer.WriteLine(
                string.Join(
                    ",",
                    EscapeCsvField(result.FullPathAndFileName),
                    result.FileSize >= 0 ? result.FileSize.ToString(CultureInfo.InvariantCulture) : "",
                    EscapeCsvField(ToSortableString(dateModified)),
                    EscapeCsvField(ToSortableString(result.DateCreated)),
                    result.Attributes.HasValue ? result.Attributes.Value.ToString(CultureInfo.InvariantCulture) : ""
                )
            );
        }

        /// <summary>
        /// EFU (Everything File Units) export per voidtools' file list layout: header line, then
        /// one row per item with filename (quoted), size in bytes, Date Modified and Date Created
        /// as 64-bit FILETIME integers, attributes as decimal. Unknown values are written as empty
        /// fields (never 0): size when negative, dates when zero/null, attributes when null.
        /// </summary>
        public static void WriteEfu(TextWriter writer, IEnumerable<SearchResult> results)
        {
            writer.WriteLine(EfuHeader);
            foreach (var result in results)
                WriteEfuRow(writer, result);
        }

        public static void WriteEfuRow(TextWriter writer, SearchResult result)
        {
            var dateModified = FileTimeToLong(result.DateModified);
            var dateCreated = result.DateCreated?.ToFileTime();

            // Everything always quotes the EFU filename column (with embedded quotes doubled).
            var filename = "\"" + result.FullPathAndFileName.Replace("\"", "\"\"") + "\"";

            writer.WriteLine(
                string.Join(
                    ",",
                    filename,
                    result.FileSize >= 0 ? result.FileSize.ToString(CultureInfo.InvariantCulture) : "",
                    dateModified.HasValue ? dateModified.Value.ToString(CultureInfo.InvariantCulture) : "",
                    dateCreated.HasValue ? dateCreated.Value.ToString(CultureInfo.InvariantCulture) : "",
                    result.Attributes.HasValue ? result.Attributes.Value.ToString(CultureInfo.InvariantCulture) : ""
                )
            );
        }

        public static void Write(TextWriter writer, IEnumerable<SearchResult> results, ExportFormat format)
        {
            switch (format)
            {
                case ExportFormat.Csv:
                    WriteCsv(writer, results);
                    break;
                case ExportFormat.Txt:
                    WriteTxt(writer, results);
                    break;
                case ExportFormat.Efu:
                    WriteEfu(writer, results);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, null);
            }
        }

        /// <summary>Combines a FILETIME's high/low parts; zero (unknown) yields null.</summary>
        public static long? FileTimeToLong(System.Runtime.InteropServices.ComTypes.FILETIME fileTime)
        {
            long value = ((long)fileTime.dwHighDateTime << 32) | (uint)fileTime.dwLowDateTime;
            return value == 0 ? null : value;
        }

        /// <summary>Local DateTime from a FILETIME; zero (unknown) yields null.</summary>
        public static DateTime? FileTimeToLocalDateTime(System.Runtime.InteropServices.ComTypes.FILETIME fileTime)
        {
            var value = FileTimeToLong(fileTime);
            return value.HasValue ? DateTime.FromFileTime(value.Value) : null;
        }

        private static string? ToSortableString(DateTime? value) =>
            value?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
