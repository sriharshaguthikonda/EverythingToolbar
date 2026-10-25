using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;

namespace EverythingToolbar.App.Search
{
    /// <summary>
    /// Streams the full result set of a query into a destination stream, paginating through the
    /// Everything client exactly like EverythingItemsProvider does (count, then 1000-item ranges).
    /// The resolved IEverythingClient (TypoFallbackClient) is intentional: exported results must
    /// match what the user sees, including typo-fallback behaviour. No UI dependencies.
    /// </summary>
    public sealed class ResultsExportService
    {
        public const int PageSize = 1000;

        private readonly IEverythingClient _everythingClient;

        public ResultsExportService(IEverythingClient everythingClient)
        {
            _everythingClient = everythingClient;
        }

        /// <summary>
        /// Exports the results of <paramref name="query"/> and returns the number of rows written.
        /// Progress (rows exported so far) is reported after each page. A superseded/canceled query
        /// returns 0 pages rather than throwing; that stops the loop cleanly and the rows written
        /// so far are kept and reported. The destination stream is flushed but not closed.
        /// </summary>
        public async Task<int> ExportAsync(
            SearchQuery query,
            Stream destination,
            ExportFormat format,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default
        )
        {
            // UTF-8 with BOM for CSV/EFU keeps Excel happy; TXT is BOM-less UTF-8 (documented
            // choice). The stream stays open (leaveOpen: true); the writer is flushed + disposed.
            var encoding = format == ExportFormat.Txt ? new UTF8Encoding(false) : new UTF8Encoding(true);
            using var writer = new StreamWriter(destination, encoding, bufferSize: 65536, leaveOpen: true);

            var totalCount = await _everythingClient.QueryCountAsync(query, PageSize, cancellationToken);
            if (cancellationToken.IsCancellationRequested || totalCount <= 0)
                return 0;

            if (format == ExportFormat.Efu)
                writer.WriteLine(ResultExporter.EfuHeader);

            var exported = 0;
            for (var startIndex = 0; startIndex < totalCount; startIndex += PageSize)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var page = await _everythingClient.QueryRangeAsync(query, startIndex, PageSize, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (page is null || page.Count == 0)
                    break;

                foreach (var result in page)
                {
                    WriteRow(writer, result, format);
                    exported++;
                }

                progress?.Report(exported);
            }

            return exported;
        }

        private static void WriteRow(TextWriter writer, SearchResult result, ExportFormat format)
        {
            switch (format)
            {
                case ExportFormat.Csv:
                    ResultExporter.WriteCsvRow(writer, result);
                    break;
                case ExportFormat.Txt:
                    ResultExporter.WriteTxtRow(writer, result);
                    break;
                case ExportFormat.Efu:
                    ResultExporter.WriteEfuRow(writer, result);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, null);
            }
        }
    }
}
