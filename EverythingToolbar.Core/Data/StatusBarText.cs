namespace EverythingToolbar.Core.Data
{
    /// <summary>
    /// Pure helper that formats the status bar count text with Everything-style query latency,
    /// e.g. "12 objects (3 ms)" or, when no latency is known, just "12 objects".
    /// </summary>
    public static class StatusBarText
    {
        public static string CountAndLatency(int total, int? milliseconds)
        {
            return milliseconds is { } ms
                ? $"{total.ToString("N0")} objects ({ms} ms)"
                : $"{total.ToString("N0")} objects";
        }
    }
}
