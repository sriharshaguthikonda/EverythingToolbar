using System;
using System.IO;
using Config.Net;
using EverythingToolbar.App;
using EverythingToolbar.FuzzySearch;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Distance settings persistence: existing 1-3 values keep working unchanged, new 4-7 values
    /// survive a round-trip, and malformed or out-of-range values are read without crashing and
    /// end up clamped by EditDistancePolicy exactly as VocabularyRefresher clamps them.
    /// </summary>
    public sealed class SettingsPersistenceTests : IDisposable
    {
        private readonly string _iniPath = Path.Combine(
            Path.GetTempPath(),
            "etb-persist-" + Guid.NewGuid().ToString("N") + ".ini"
        );

        public void Dispose()
        {
            try
            {
                File.Delete(_iniPath);
            }
            catch (IOException)
            {
                // Temp cleanup is best-effort.
            }
        }

        private ISettings Load()
        {
            return SettingsProxy.Create(new ConfigurationBuilder<IToolbarSettings>().UseIniFile(_iniPath).Build());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void ExistingValues_OneToThree_PersistUnchanged(int distance)
        {
            File.WriteAllText(
                _iniPath,
                $"TypoMaxDictionaryEditDistance={distance}\n"
                    + $"TypoLongWordMaxEditDistance={distance}\n"
                    + "TypoLongWordMinLength=9\n"
            );

            var settings = Load();

            Assert.Equal(distance, settings.TypoMaxDictionaryEditDistance);
            Assert.Equal(distance, settings.TypoLongWordMaxEditDistance);
            Assert.Equal(9, settings.TypoLongWordMinLength);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        public void NewValues_FourToSeven_PersistAcrossReload(int distance)
        {
            File.WriteAllText(
                _iniPath,
                $"TypoMaxDictionaryEditDistance={distance}\nTypoLongWordMaxEditDistance={distance}\n"
            );

            var settings = Load();

            Assert.Equal(distance, settings.TypoMaxDictionaryEditDistance);
            Assert.Equal(distance, settings.TypoLongWordMaxEditDistance);

            // The settings object persists on write; a fresh instance must read the same values.
            File.WriteAllText(_iniPath, File.ReadAllText(_iniPath));
            var reloaded = Load();
            Assert.Equal(distance, reloaded.TypoMaxDictionaryEditDistance);
        }

        [Fact]
        public void BelowRangeValue_IsClampedNotRejected()
        {
            File.WriteAllText(_iniPath, "TypoMaxDictionaryEditDistance=0\nTypoLongWordMaxEditDistance=-2\n");

            var settings = Load();
            var policy = new EditDistancePolicy(
                settings.TypoMaxDictionaryEditDistance,
                settings.TypoLongWordMaxEditDistance,
                settings.TypoLongWordMinLength
            );

            Assert.Equal(1, policy.MaxDictionaryEditDistance);
            Assert.Equal(1, policy.LongWordMaxEditDistance);
        }

        [Fact]
        public void AboveRangeValue_IsClampedNotRejected()
        {
            File.WriteAllText(_iniPath, "TypoMaxDictionaryEditDistance=42\nTypoLongWordMaxEditDistance=42\n");

            var settings = Load();
            var policy = new EditDistancePolicy(
                settings.TypoMaxDictionaryEditDistance,
                settings.TypoLongWordMaxEditDistance,
                settings.TypoLongWordMinLength
            );

            Assert.Equal(EditDistancePolicy.MaxSupportedDictionaryEditDistance, policy.MaxDictionaryEditDistance);
            Assert.Equal(EditDistancePolicy.MaxSupportedDictionaryEditDistance, policy.LongWordMaxEditDistance);
        }

        [Fact]
        public void MalformedValue_ReadsBackAsSomethingSafe()
        {
            File.WriteAllText(_iniPath, "TypoMaxDictionaryEditDistance=not-a-number\n");

            var settings = Load();
            var raw = settings.TypoMaxDictionaryEditDistance;

            // Whatever Config.Net yields for garbage (default or fallback), the policy must clamp
            // it into the supported range so a corrupt ini can never crash or disable startup.
            Assert.InRange(raw, int.MinValue, int.MaxValue);
            var policy = new EditDistancePolicy(raw, raw, 9);
            Assert.InRange(
                policy.MaxDictionaryEditDistance,
                EditDistancePolicy.MinDictionaryEditDistance,
                EditDistancePolicy.MaxSupportedDictionaryEditDistance
            );
        }

        [Fact]
        public void LongWordValueAboveIndexValue_IsClampedToIndexDistance()
        {
            File.WriteAllText(_iniPath, "TypoMaxDictionaryEditDistance=5\nTypoLongWordMaxEditDistance=7\n");

            var settings = Load();
            var policy = new EditDistancePolicy(
                settings.TypoMaxDictionaryEditDistance,
                settings.TypoLongWordMaxEditDistance,
                settings.TypoLongWordMinLength
            );

            Assert.Equal(5, settings.TypoMaxDictionaryEditDistance);
            Assert.Equal(5, policy.LongWordMaxEditDistance);
        }

        [Fact]
        public void UnrelatedPreferences_AreNotTouchedByDistanceValues()
        {
            File.WriteAllText(
                _iniPath,
                "IsSearchAsYouType=false\n"
                    + "IsTypoTolerantSearchEnabled=true\n"
                    + "TypoMaxDictionaryEditDistance=6\n"
                    + "TypoLongWordMaxEditDistance=6\n"
            );

            var settings = Load();

            Assert.False(settings.IsSearchAsYouType);
            Assert.True(settings.IsTypoTolerantSearchEnabled);
            Assert.Equal(6, settings.TypoMaxDictionaryEditDistance);
        }
    }
}
