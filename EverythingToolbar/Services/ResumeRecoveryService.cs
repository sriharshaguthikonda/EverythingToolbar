using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using NLog;

namespace EverythingToolbar.Services
{
    /// <summary>
    /// Recovers search after sleep/hibernate: on resume the SDK3 named pipe may be dead or stale
    /// and Everything's own service may still be starting. On Resume we drop the pipe client and
    /// any cached result list immediately (pure state reset, no I/O), then — after a short grace
    /// delay on a background thread so Everything can finish waking up — probe a reconnect and
    /// rebuild the active search session so visible results refresh. The grace delay lives here,
    /// off the per-keystroke query path; the pipe client itself stays synchronous and sleep-free.
    /// </summary>
    public sealed class ResumeRecoveryService : IDisposable
    {
        private static readonly TimeSpan ResumeGraceDelay = TimeSpan.FromSeconds(1);
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        private readonly EverythingPipeClient _pipeClient;
        private readonly SearchSession _searchSession;
        private readonly Dispatcher _dispatcher;
        private bool _started;

        public ResumeRecoveryService(EverythingPipeClient pipeClient, SearchSession searchSession)
        {
            _pipeClient = pipeClient;
            _searchSession = searchSession;
            // AppServices.Initialize runs on the UI thread (same pattern as ThemeService).
            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        /// <summary>Subscribes to power-mode changes once; safe to call repeatedly.</summary>
        public void Start()
        {
            if (_started)
                return;
            _started = true;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume)
                return;

            try
            {
                // Drop the stale connection and cached results up front so nothing stale is served.
                _pipeClient.InvalidateForResume();

                Task.Run(async () =>
                {
                    try
                    {
                        // An immediate reconnect would likely race Everything's own startup; the
                        // existing reconnect-once retry inside a query is not enough on its own.
                        await Task.Delay(ResumeGraceDelay).ConfigureAwait(false);
                        _pipeClient.TryConnect();
                        _dispatcher.Invoke(_searchSession.Start);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "Search recovery after resume failed");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Search resume handling failed");
            }
        }

        public void Dispose()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
    }
}
