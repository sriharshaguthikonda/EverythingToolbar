using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class TokenVocabularyTests
    {
        [Fact]
        public void AddPath_TracksWordsCompoundsAndDisplay()
        {
            var vocabulary = new TokenVocabulary();
            vocabulary.AddPath(@"C:\Tools\RepoMaps\RepoMaps_Notes.md");

            Assert.Contains(vocabulary.WordsByFrequency(), e => e.Normalized == "repo");
            var compound = vocabulary.Find("repomaps_notes");
            Assert.NotNull(compound);
            Assert.True(compound!.IsCompound);
            Assert.Equal("RepoMaps_Notes", compound.Display);
        }

        [Fact]
        public void Find_UnknownWord_ReturnsNull()
        {
            Assert.Null(new TokenVocabulary().Find("zzznotfound"));
        }

        [Fact]
        public void AddPath_IncrementsFrequency()
        {
            var vocabulary = new TokenVocabulary();
            vocabulary.AddPath(@"C:\a\ollama\ollama.exe");
            vocabulary.AddPath(@"C:\b\ollama\README.md");

            var entry = vocabulary.Find("ollama");
            Assert.NotNull(entry);
            Assert.True(entry!.Frequency >= 3);
            Assert.Equal("ollama", entry.Display);
        }

        [Fact]
        public void WordsByFrequency_IsSortedDescending()
        {
            var vocabulary = new TokenVocabulary();
            vocabulary.AddPath(@"C:\a\frequent\frequent\frequent.txt");
            vocabulary.AddPath(@"C:\b\rare.txt");

            var words = vocabulary.WordsByFrequency();

            Assert.True(words.First().Frequency >= words.Last().Frequency);
            Assert.Equal("frequent", words.First().Normalized);
        }

        [Fact]
        public void AddPaths_ConcurrentAdds_AreThreadSafe()
        {
            var vocabulary = new TokenVocabulary();

            Parallel.For(
                0,
                8,
                _ =>
                {
                    for (var j = 0; j < 50; j++)
                    {
                        vocabulary.AddPath(@"C:\common\word.txt");
                    }
                }
            );

            Assert.Equal(4, vocabulary.WordCount);
            Assert.Equal(400, vocabulary.Find("common")!.Frequency);
            Assert.Equal(400, vocabulary.Find("word")!.Frequency);
        }
    }
}
