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
    /// One CSV export column: header text plus a value selector. Values returning null (or an
    /// empty string) are written as empty CSV fields — never 0. Lambdas are non-capturing, so
    /// the compiler caches each delegate; constructing the standard columns allocates once.
    /// </summary>
    public readonly record struct ExportColumn(string Header, Func<SearchResult, string?> Value);

    /// <summary>
    /// Pure, testable writer for search-result exports. Exposes whole-enumeration writers (which
    /// stream without materializing the set) plus per-row writers so services can interleave
    /// paging with writing. All text uses the encoding of the caller's TextWriter; the export
    /// service picks UTF-8 with a BOM for CSV/EFU (Excel friendliness — documented choice).
    /// </summary>
    public static class ResultExporter
    {
        public static readonly ExportColumn FilenameColumn = new("Filename", r => r.FullPathAndFileName);

        public static readonly ExportColumn PathColumn = new("Path", r => r.Path);

        public static readonly ExportColumn NameColumn = new("Name", r => r.FileName);

        public static readonly ExportColumn ExtensionColumn = new("Extension", r => r.Extension);

        public static readonly ExportColumn SizeColumn = new(
            "Size",
            r => r.FileSize >= 0 ? r.FileSize.ToString(CultureInfo.InvariantCulture) : null
        );

        public static readonly ExportColumn DateModifiedColumn = new(
            "Date Modified",
            r => ToSortableString(FileTimeToLocalDateTime(r.DateModified))
        );

        public static readonly ExportColumn DateCreatedColumn = new(
            "Date Created",
            r => ToSortableString(r.DateCreated)
        );

        public static readonly ExportColumn AttributesColumn = new(
            "Attributes",
            r => r.Attributes?.ToString(CultureInfo.InvariantCulture)
        );

        public static readonly ExportColumn TypeColumn = new("Type", r => r.TypeName);

        /// <summary>
        /// Every column the Export dialog offers, in fixed display order. CSV column choice is a
        /// checked subset of this list; the checked set is the choice (no reordering — output
        /// always follows this order).
        /// </summary>
        public static readonly IReadOnlyList<ExportColumn> AvailableColumns = new[]
        {
            FilenameColumn,
            PathColumn,
            NameColumn,
            ExtensionColumn,
            SizeColumn,
            DateModifiedColumn,
            DateCreatedColumn,
            AttributesColumn,
            TypeColumn,
        };

        /// <summary>The historical (EFU-parity) default CSV column set.</summary>
        public static readonly IReadOnlyList<ExportColumn> DefaultCsvColumns = new[]
        {
            FilenameColumn,
            SizeColumn,
            DateModifiedColumn,
            DateCreatedColumn,
            AttributesColumn,
        };

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
            WriteCsv(writer, results, DefaultCsvColumns);
        }

        /// <summary>
        /// CSV export with an explicit column set: header line from the columns' headers (in the
        /// given order, RFC 4180 quoted) plus one row per item. Column order in the output is the
        /// order of <paramref name="columns"/>.
        /// </summary>
        public static void WriteCsv(
            TextWriter writer,
            IEnumerable<SearchResult> results,
            IReadOnlyList<ExportColumn> columns
        )
        {
            writer.WriteLine(CsvHeaderFor(columns));
            foreach (var result in results)
                WriteCsvRow(writer, result, columns);
        }

        /// <summary>Header line for the given column set (escaped, comma-joined).</summary>
        public static string CsvHeaderFor(IReadOnlyList<ExportColumn> columns)
        {
            var headers = new string[columns.Count];
            for (var i = 0; i < columns.Count; i++)
                headers[i] = EscapeCsvField(columns[i].Header);
            return string.Join(",", headers);
        }

        public static void WriteCsvRow(TextWriter writer, SearchResult result)
        {
            WriteCsvRow(writer, result, DefaultCsvColumns);
        }

        public static void WriteCsvRow(TextWriter writer, SearchResult result, IReadOnlyList<ExportColumn> columns)
        {
            var fields = new string[columns.Count];
            for (var i = 0; i < columns.Count; i++)
                fields[i] = EscapeCsvField(columns[i].Value(result));
            writer.WriteLine(string.Join(",", fields));
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
