using EverythingToolbar.Core.Data;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class StatusBarTextTests
    {
        [Theory]
        [InlineData(0, null, "0 objects")]
        [InlineData(0, 0, "0 objects (0 ms)")]
        [InlineData(1, 3, "1 objects (3 ms)")]
        [InlineData(1234, 42, "1,234 objects (42 ms)")]
        [InlineData(1234567, 999, "1,234,567 objects (999 ms)")]
        public void CountAndLatency_FormatsLikeEverything(int total, int? ms, string expected)
        {
            Assert.Equal(expected, StatusBarText.CountAndLatency(total, ms));
        }
    }
}
