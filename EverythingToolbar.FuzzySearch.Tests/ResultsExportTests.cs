using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EverythingToolbar.App.Search;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;
using EverythingToolbar.FuzzySearch.Tests.Support;
using Xunit;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class ResultExporterTests
    {
        private static SearchResult Result(
            string path,
            long size = -1,
            long dateModified = 0,
            DateTime? dateCreated = null,
            uint? attributes = null
        )
        {
            return new SearchResult(
                path,
                Path.GetFileName(path),
                path,
                IsFile: true,
                FileSize: size,
                new FILETIME
                {
                    dwHighDateTime = (int)(dateModified >> 32),
                    dwLowDateTime = (int)(dateModified & 0xFFFFFFFF),
                },
                DateCreated: dateCreated,
                Attributes: attributes
            );
        }

        private static string Write(Action<TextWriter> write)
        {
            var builder = new StringBuilder();
            using (var writer = new StringWriter(builder))
                write(writer);
            return builder.ToString().Replace("\r\n", "\n");
        }

        [Fact]
        public void EscapeCsvField_QuotesFieldsWithSpecials()
        {
            Assert.Equal("plain", ResultExporter.EscapeCsvField("plain"));
            Assert.Equal("\"a,b\"", ResultExporter.EscapeCsvField("a,b"));
            Assert.Equal("\"say \"\"hi\"\"\"", ResultExporter.EscapeCsvField("say \"hi\""));
            Assert.Equal("\"line1\nline2\"", ResultExporter.EscapeCsvField("line1\nline2"));
            Assert.Equal("\"carriage\rreturn\"", ResultExporter.EscapeCsvField("carriage\rreturn"));
            Assert.Equal("", ResultExporter.EscapeCsvField(null));
            Assert.Equal("", ResultExporter.EscapeCsvField(""));
        }

        [Fact]
        public void WriteCsv_WritesHeaderAndMetadataColumns()
        {
            // 2020-01-01 00:00:00 UTC in FILETIME ticks (100ns since 1601-01-01).
            const long filetime = 132244224000000000;
            var expectedLocal = DateTime
                .FromFileTime(filetime)
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var output = Write(w =>
                ResultExporter.WriteCsv(
                    w,
                    new[]
                    {
                        Result(
                            @"C:\simple\file.txt",
                            size: 12345,
                            dateModified: filetime,
                            dateCreated: DateTime.FromFileTime(filetime),
                            attributes: 32
                        ),
                        Result(@"C:\dir with, comma\name""quote.txt"),
                    }
                )
            );

            var lines = output.Split('\n');
            Assert.Equal(ResultExporter.CsvHeader, lines[0]);
            Assert.Equal($"C:\\simple\\file.txt,12345,{expectedLocal},{expectedLocal},32", lines[1]);
            // Unknown values are empty fields; the comma-containing path is the only quoted one.
            Assert.Equal("\"C:\\dir with, comma\\name\"\"quote.txt\",,,,", lines[2]);
        }

        [Fact]
        public void WriteTxt_WritesOneFullPathPerLine()
        {
            var output = Write(w =>
                ResultExporter.WriteTxt(w, new[] { Result(@"C:\a\b.txt"), Result(@"C:\c,d\e.txt") })
            );

            Assert.Equal("C:\\a\\b.txt\nC:\\c,d\\e.txt\n", output);
        }

        [Fact]
        public void WriteEfu_WritesHeaderAndFiletimeRows()
        {
            // 2020-01-01 00:00:00 UTC in FILETIME ticks (100ns since 1601-01-01).
            const long filetime = 132244224000000000;
            var dateCreated = DateTime.FromFileTime(filetime); // round-trips via ToFileTime()
            var output = Write(w =>
                ResultExporter.WriteEfu(
                    w,
                    new[]
                    {
                        Result(
                            @"C:\full\data.bin",
                            size: 12345,
                            dateModified: filetime,
                            dateCreated: dateCreated,
                            attributes: 32
                        ),
                        Result(@"C:\unknown\everything.bin", size: -1, dateModified: 0),
                    }
                )
            );

            var lines = output.Split('\n');
            Assert.Equal(ResultExporter.EfuHeader, lines[0]);
            Assert.Equal($"\"C:\\full\\data.bin\",12345,{filetime},{dateCreated.ToFileTime()},32", lines[1]);
            // The filename column is always quoted, even when quoting is not required.
            // Unknown size / date / attributes become empty fields, never 0.
            Assert.Equal("\"C:\\unknown\\everything.bin\",,,,", lines[2]);
        }

        [Fact]
        public void WriteEfu_QuotesFilenamesWithCommasAndQuotes()
        {
            var output = Write(w =>
                ResultExporter.WriteEfu(w, new[] { Result("C:\\a,b\"c.txt", size: 1, dateModified: 5) })
            );

            var lines = output.Split('\n');
            Assert.Equal("\"C:\\a,b\"\"c.txt\",1,5,,", lines[1]);
        }

        [Fact]
        public void FileTimeToLong_CombinesHighLowAndTreatsZeroAsUnknown()
        {
            const long value = 132244224000000000;
            var fileTime = new FILETIME
            {
                dwHighDateTime = (int)(value >> 32),
                dwLowDateTime = unchecked((int)(value & 0xFFFFFFFFL)),
            };

            Assert.Equal(value, ResultExporter.FileTimeToLong(fileTime));
            Assert.Null(ResultExporter.FileTimeToLong(default));
        }
    }

    public class ResultsExportServiceTests
    {
        private static SearchResult Result(string path)
        {
            return new SearchResult(path, Path.GetFileName(path), path, IsFile: true, FileSize: 0, default);
        }

        private static string ReadAll(MemoryStream stream)
        {
            stream.Position = 0;
            return new StreamReader(stream, Encoding.UTF8).ReadToEnd();
        }

        [Fact]
        public async Task ExportAsync_PagesThroughAllResultsIn1000Batches()
        {
            var corpus = Enumerable.Range(0, 2500).Select(i => $@"C:\data\file{i}.txt").ToList();
            var client = new FakeEverythingClient { PathCorpus = corpus, Results = new List<SearchResult>() };
            var service = new ResultsExportService(client);
            using var stream = new MemoryStream();

            var query = new SearchQuery("file", SortBy.Name, false, false, false, false, false);
            var exported = await service.ExportAsync(query, stream, ExportFormat.Txt);

            Assert.Equal(2500, exported);
            Assert.Equal(3, client.CorpusRangeQueries); // ceil(2500 / 1000)
            Assert.Equal(2500, ReadAll(stream).Count(line => line == '\n'));
        }

        [Fact]
        public async Task ExportAsync_ReportsProgressPerBatch()
        {
            var corpus = Enumerable.Range(0, 2500).Select(i => $@"C:\data\file{i}.txt").ToList();
            var client = new FakeEverythingClient { PathCorpus = corpus };
            var service = new ResultsExportService(client);
            using var stream = new MemoryStream();

            var reports = new List<int>();
            var progress = new SynchronousProgress(count => reports.Add(count));
            var query = new SearchQuery("file", SortBy.Name, false, false, false, false, false);
            await service.ExportAsync(query, stream, ExportFormat.Txt, progress);

            Assert.Equal(new[] { 1000, 2000, 2500 }, reports);
        }

        [Fact]
        public async Task ExportAsync_CanceledMidStreamStopsCleanly()
        {
            var corpus = Enumerable.Range(0, 5000).Select(i => $@"C:\data\file{i}.txt").ToList();
            var client = new CancelingFakeClient(corpus);
            var service = new ResultsExportService(client);
            using var stream = new MemoryStream();

            var reports = new List<int>();
            using var cts = new CancellationTokenSource();
            var progress = new SynchronousProgress(count =>
            {
                reports.Add(count);
                if (count >= 2000)
                    cts.Cancel();
            });
            var query = new SearchQuery("file", SortBy.Name, false, false, false, false, false);
            var exported = await service.ExportAsync(query, stream, ExportFormat.Txt, progress, cts.Token);

            // The token is canceled after page 2; the service stops before issuing page 3,
            // keeping the 2000 rows already written.
            Assert.Equal(2000, exported);
            Assert.Equal(2, client.RangeQueries);
            Assert.Equal(new[] { 1000, 2000 }, reports);
            Assert.Equal(2000, ReadAll(stream).Count(line => line == '\n'));
        }

        [Fact]
        public async Task ExportAsync_ZeroCountWritesNothing()
        {
            var client = new FakeEverythingClient { CountToReturn = 0 };
            var service = new ResultsExportService(client);
            using var stream = new MemoryStream();

            var query = new SearchQuery("nothing", SortBy.Name, false, false, false, false, false);
            var exported = await service.ExportAsync(query, stream, ExportFormat.Efu);

            Assert.Equal(0, exported);
            Assert.Equal(0, client.CorpusRangeQueries);
            Assert.Equal("", ReadAll(stream));
        }

        private sealed class SynchronousProgress : IProgress<int>
        {
            private readonly Action<int> _callback;

            public SynchronousProgress(Action<int> callback)
            {
                _callback = callback;
            }

            public void Report(int value)
            {
                _callback(value);
            }
        }

        /// <summary>
        /// Fake client honoring the interface contract that a canceled query returns 0 results
        /// rather than throwing.
        /// </summary>
        private sealed class CancelingFakeClient : IEverythingClient
        {
            private readonly IReadOnlyList<string> _corpus;

            public CancelingFakeClient(IReadOnlyList<string> corpus)
            {
                _corpus = corpus;
            }

            public int RangeQueries { get; private set; }

            public Task<int> QueryCountAsync(SearchQuery query, int pageSize, CancellationToken cancellationToken) =>
                Task.FromResult(_corpus.Count);

            public int QueryCountSync(SearchQuery query, int pageSize, CancellationToken cancellationToken) =>
                _corpus.Count;

            public Task<IList<SearchResult>> QueryRangeAsync(
                SearchQuery query,
                int startIndex,
                int pageSize,
                CancellationToken cancellationToken
            )
            {
                RangeQueries++;
                IList<SearchResult> page = cancellationToken.IsCancellationRequested
                    ? new List<SearchResult>()
                    : _corpus.Skip(startIndex).Take(pageSize).Select(Result).ToList();

                return Task.FromResult(page);
            }

            public IList<SearchResult> QueryRangeSync(
                SearchQuery query,
                int startIndex,
                int pageSize,
                CancellationToken cancellationToken
            )
            {
                return QueryRangeAsync(query, startIndex, pageSize, cancellationToken).GetAwaiter().GetResult();
            }

            public bool TryReadCachedFirstPage(SearchQuery query, out IList<SearchResult> results)
            {
                results = new List<SearchResult>();
                return false;
            }

            public Version GetEverythingVersion() => new(1, 5, 0);

            public void SetInstanceName(string name) { }

            public void IncrementRunCount(string path) { }

            public bool GetIsFastSort(SortBy sortBy, bool descending) => false;
        }
    }
}
