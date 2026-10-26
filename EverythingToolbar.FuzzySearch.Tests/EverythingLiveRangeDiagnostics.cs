using System;
using System.IO;
using System.Runtime.InteropServices;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;
using EverythingToolbar.Platform.Search;
using Xunit;
using Xunit.Abstractions;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Diagnostic-only live probe (LIVE_EVERYTHING=1, elevated): isolates why QueryRangeSync can
    /// return zero rows while the count for the same query is non-zero. Runs the production
    /// search configuration step by step against the real SDK3 pipe and prints total count,
    /// viewport count and last error for each variant. No product code is changed by this test.
    /// </summary>
    [Trait("Category", "LiveEverything")]
    public class EverythingLiveRangeDiagnostics
    {
        private const uint PropertyIdName = 0;
        private const uint PropertyIdPath = 1;
        private const uint PropertyIdSize = 2;
        private const uint PropertyIdDateModified = 5;
        private const uint PropertyIdPathAndName = 240;

        private readonly ITestOutputHelper _output;

        public EverythingLiveRangeDiagnostics(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Probe_RangeViewportBehaviour()
        {
            if (Environment.GetEnvironmentVariable("LIVE_EVERYTHING") is null)
            {
                _output.WriteLine("Skipped: set LIVE_EVERYTHING=1 with Everything 1.5a running.");
                return;
            }

            var testDir = Path.Combine(Path.GetTempPath(), "etb-range-probe-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "ProbeAlpha_ONE.txt"), "x");
            File.WriteAllText(Path.Combine(testDir, "ProbeBeta_TWO.txt"), "x");

            try
            {
                var pipeClient = new EverythingPipeClient();
                Assert.True(pipeClient.TryConnect(), "pipe must be reachable (run elevated)");
                _output.WriteLine("Everything version: " + pipeClient.GetEverythingVersion());

                var query = new SearchQuery("path:\"" + testDir + "\"", SortBy.Name, false, false, false, false, false);

                var count = pipeClient.QueryCountSync(query, 256, default);
                var rows = pipeClient.QueryRangeSync(query, 0, 256, default);
                _output.WriteLine($"[PROBE] production path: count={count} rangeRows={rows.Count}");

                // Variant A: minimal request - no sort, single property request.
                Raw("A no-sort minimal", "path:\"" + testDir + "\"", withSort: false, fullRequests: false);

                // Variant B: production sort added.
                Raw("B no-sort? sort-only", "path:\"" + testDir + "\"", withSort: true, fullRequests: false);

                // Variant C: full production property requests.
                Raw("C full production", "path:\"" + testDir + "\"", withSort: true, fullRequests: true);

                // Variant D: plain text query without path syntax.
                Raw("D plain text", "ProbeAlpha", withSort: true, fullRequests: true);

                // Variant E: bigger viewport request.
                Raw("E viewport 16", "path:\"" + testDir + "\"", withSort: true, fullRequests: true, viewport: 16);

                Assert.True(count > 0, "exact query must find the probe files");
            }
            finally
            {
                Directory.Delete(testDir, recursive: true);
            }
        }

        private void Raw(string label, string searchText, bool withSort, bool fullRequests, int viewport = 256)
        {
            // Same connection order as the production pipe client: default instance first,
            // then the alpha instance the installed 1.5a build exposes.
            var client = Everything3_ConnectW(null);
            if (client == IntPtr.Zero)
            {
                client = Everything3_ConnectW("1.5a");
            }

            Assert.True(client != IntPtr.Zero, "raw connect failed");

            try
            {
                var state = Everything3_CreateSearchState();
                Assert.True(state != IntPtr.Zero, "search state alloc failed");

                try
                {
                    Everything3_SetSearchTextW(state, searchText);
                    if (withSort)
                    {
                        Everything3_AddSearchSort(state, PropertyIdName, ascending: true);
                    }

                    if (fullRequests)
                    {
                        Everything3_AddSearchPropertyRequest(state, PropertyIdPathAndName);
                        Everything3_AddSearchPropertyRequestHighlighted(state, PropertyIdName);
                        Everything3_AddSearchPropertyRequestHighlighted(state, PropertyIdPath);
                        Everything3_AddSearchPropertyRequest(state, PropertyIdSize);
                        Everything3_AddSearchPropertyRequest(state, PropertyIdDateModified);
                    }
                    else
                    {
                        Everything3_AddSearchPropertyRequest(state, PropertyIdPathAndName);
                    }

                    Everything3_SetSearchViewportOffset(state, 0);
                    Everything3_SetSearchViewportCount(state, (nuint)viewport);

                    var resultList = Everything3_Search(client, state);
                    if (resultList == IntPtr.Zero)
                    {
                        _output.WriteLine($"[PROBE] {label}: SEARCH FAILED error=0x{Everything3_GetLastError():X8}");
                        return;
                    }

                    try
                    {
                        var total = Everything3_GetResultListCount(resultList);
                        var viewportCount = Everything3_GetResultListViewportCount(resultList);
                        var error = Everything3_GetLastError();
                        _output.WriteLine(
                            $"[PROBE] {label}: total={total} viewport={viewportCount} error=0x{error:X8}"
                        );
                    }
                    finally
                    {
                        Everything3_DestroyResultList(resultList);
                    }
                }
                finally
                {
                    Everything3_DestroySearchState(state);
                }
            }
            finally
            {
                Everything3_DestroyClient(client);
            }
        }

        [DllImport("Everything3.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr Everything3_ConnectW(string? lpInstanceName);

        [DllImport("Everything3.dll")]
        private static extern bool Everything3_DestroyClient(IntPtr client);

        [DllImport("Everything3.dll")]
        private static extern uint Everything3_GetLastError();

        [DllImport("Everything3.dll")]
        private static extern IntPtr Everything3_CreateSearchState();

        [DllImport("Everything3.dll")]
        private static extern bool Everything3_DestroySearchState(IntPtr searchState);

        [DllImport("Everything3.dll", CharSet = CharSet.Unicode)]
        private static extern bool Everything3_SetSearchTextW(IntPtr searchState, string lpSearchText);

        [DllImport("Everything3.dll")]
        private static extern bool Everything3_AddSearchSort(IntPtr searchState, uint propertyId, bool ascending);

        [DllImport("Everything3.dll")]
        private static extern bool Everything3_AddSearchPropertyRequest(IntPtr searchState, uint propertyId);

        [DllImport("Everything3.dll")]
        private static extern bool Everything3_AddSearchPropertyRequestHighlighted(IntPtr searchState, uint propertyId);

        [DllImport("Everything3.dll")]
        private static extern bool Everything3_SetSearchViewportOffset(IntPtr searchState, nuint offset);

        [DllImport("Everything3.dll")]
        private static extern bool Everything3_SetSearchViewportCount(IntPtr searchState, nuint count);

        [DllImport("Everything3.dll")]
        private static extern IntPtr Everything3_Search(IntPtr client, IntPtr searchState);

        [DllImport("Everything3.dll")]
        private static extern bool Everything3_DestroyResultList(IntPtr resultList);

        [DllImport("Everything3.dll")]
        private static extern nuint Everything3_GetResultListCount(IntPtr resultList);

        [DllImport("Everything3.dll")]
        private static extern nuint Everything3_GetResultListViewportCount(IntPtr resultList);
    }
}
