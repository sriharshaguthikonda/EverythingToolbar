using System;
using System.IO;
using Config.Net;
using EverythingToolbar.App;

namespace EverythingToolbar.FuzzySearch.Tests.Support
{
    /// <summary>Real Config.Net settings backed by a temp ini, so TypoFallbackClient tests drive the production settings path.</summary>
    public static class TestSettingsFactory
    {
        public static ISettings Create(bool typoTolerantSearchEnabled)
        {
            var path = Path.Combine(Path.GetTempPath(), "etb-settings-" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllText(
                path,
                "IsTypoTolerantSearchEnabled=" + (typoTolerantSearchEnabled ? "true" : "false") + Environment.NewLine
            );
            var settings = SettingsProxy.Create(new ConfigurationBuilder<IToolbarSettings>().UseIniFile(path).Build());
            if (settings.IsTypoTolerantSearchEnabled != typoTolerantSearchEnabled)
            {
                throw new InvalidOperationException(
                    "Test settings factory could not persist IsTypoTolerantSearchEnabled=" + typoTolerantSearchEnabled
                );
            }

            return settings;
        }
    }
}
