using System.Collections.Generic;
using System.Threading.Tasks;
using EverythingToolbar.App.Search;
using EverythingToolbar.Core.Data;
using EverythingToolbar.FuzzySearch.Tests.Support;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class EverythingItemsProviderTests
    {
        private static SearchResult Result(string fullPath)
        {
            return new SearchResult(fullPath, fullPath, fullPath, IsFile: true, FileSize: 0, default);
        }

        [Fact]
        public async Task FetchRange_FeedsMaterializedResultsToCallback()
        {
            var client = new FakeEverythingClient
            {
                Results = new List<SearchResult>
                {
                    Result(@"C:\Tools\Neuroscience_Project\notes.txt"),
                    Result(@"C:\Work\ollama\ollama.exe"),
                },
            };
            var vocabulary = new TokenVocabulary();
            var materialized = new List<IList<SearchResult>>();
            var provider = new EverythingItemsProvider(
                client,
                new EverythingToolbar.Core.Search.SearchQuery("notes", default, false, false, false, false, false),
                results => materialized.Add(results)
            );

            var page = await provider.FetchRange(0, 256, isAsync: false, System.Threading.CancellationToken.None);

            Assert.Equal(2, page.Count);
            var single = Assert.Single(materialized);
            Assert.Equal(2, single.Count);

            vocabulary.AddPath(@"C:\Tools\Neuroscience_Project\notes.txt");
            Assert.NotNull(vocabulary.Find("neuroscience"));
        }

        [Fact]
        public async Task FetchCount_DoesNotTriggerCallback()
        {
            var client = new FakeEverythingClient { CountToReturn = 5 };
            var called = false;
            var provider = new EverythingItemsProvider(
                client,
                new EverythingToolbar.Core.Search.SearchQuery("notes", default, false, false, false, false, false),
                _ => called = true
            );

            var count = await provider.FetchCount(256, isAsync: false, System.Threading.CancellationToken.None);

            Assert.Equal(5, count);
            Assert.False(called);
        }

        [Fact]
        public async Task FetchRange_WorksWithoutCallback()
        {
            var client = new FakeEverythingClient { Results = new List<SearchResult> { Result(@"C:\a.txt") } };
            var provider = new EverythingItemsProvider(
                client,
                new EverythingToolbar.Core.Search.SearchQuery("a", default, false, false, false, false, false)
            );

            var page = await provider.FetchRange(0, 256, isAsync: false, System.Threading.CancellationToken.None);

            Assert.Single(page);
        }
    }
}
