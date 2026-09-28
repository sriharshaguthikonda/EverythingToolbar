using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class FilenameTokenizerTests
    {
        private readonly FilenameTokenizer _tokenizer = new();

        [Theory]
        [InlineData("Neuroscience_Project", "Neuroscience", "Project")]
        [InlineData("PowerToys", "Power", "Toys")]
        [InlineData("HTMLParser", "HTML", "Parser")]
        [InlineData("HTMLParser2", "HTML", "Parser", "2")]
        [InlineData("repo-map", "repo", "map")]
        [InlineData("repo_maps", "repo", "maps")]
        [InlineData("qwen3", "qwen", "3")]
        [InlineData("file.name.txt", "file", "name", "txt")]
        [InlineData("naïveFile", "naïve", "File")]
        [InlineData("AutoHotkey", "Auto", "Hotkey")]
        public void TokenizeWords_SplitsSeparatorsCamelCaseAndDigitBoundaries(string text, params string[] expected)
        {
            Assert.Equal(expected, _tokenizer.TokenizeWords(text));
        }

        [Fact]
        public void TokenizeWords_EmptyInput_YieldsNothing()
        {
            Assert.Empty(_tokenizer.TokenizeWords(""));
            Assert.Empty(_tokenizer.TokenizeWords("   "));
        }

        [Fact]
        public void TokenizePath_SplitsSegmentsAndStripsExtensionFromCompound()
        {
            var (words, compounds) = _tokenizer.TokenizePath(@"C:\Tools\RepoMaps\readme.md");

            Assert.Contains("C", words);
            Assert.Contains("Tools", words);
            Assert.Contains("Repo", words);
            Assert.Contains("Maps", words);
            Assert.Contains("readme", words);
            Assert.Contains("md", words);
            Assert.Contains("RepoMaps", compounds);
            Assert.Contains("readme", compounds);
            Assert.DoesNotContain(compounds, c => c.Contains('.'));
        }

        [Fact]
        public void TokenizePath_PreservesCompoundCasing()
        {
            var (_, compounds) = _tokenizer.TokenizePath(@"D:\RepoMaps_Notes.md");

            Assert.Contains("RepoMaps_Notes", compounds);
        }
    }
}
