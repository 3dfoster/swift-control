using System;
using System.Diagnostics;
using System.Threading;

namespace SwiftControl
{
    internal sealed class LightingCoordinator : IDisposable
    {
        private readonly object _sync = new object();
        private bool _disposed;
        private int _generation;
        private Timer _terminationTimer;

        public void NotifyCodexComplete()
        {
            TouchpadLightingSettings settings = TouchpadLightingSettings.Load();
            if (!settings.CodexEnabled) return;

            int generation;
            lock (_sync)
            {
                if (_disposed) return;
                generation = ++_generation;
                DisposeTerminationTimer();
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                if (!IsCurrent(generation)) return;
                TryLighting(delegate(AcerLightingClient lighting)
                {
                    if (!IsCurrent(generation)) return;
                    lighting.TerminateEffect();
                    Thread.Sleep(75);
                    if (IsCurrent(generation))
                        lighting.PlayEffect(settings.CodexEffect);
                });

                lock (_sync)
                {
                    if (_disposed || generation != _generation) return;
                    _terminationTimer = new Timer(
                        delegate { Terminate(generation); }, null,
                        TouchpadLightingSettings.PreviewDuration(settings.CodexEffect),
                        Timeout.Infinite);
                }
            });
        }

        private void Terminate(int generation)
        {
            if (!IsCurrent(generation)) return;
            TryLighting(delegate(AcerLightingClient lighting)
            {
                if (IsCurrent(generation)) lighting.TerminateEffect();
            });
            lock (_sync)
            {
                if (generation == _generation) DisposeTerminationTimer();
            }
        }

        private bool IsCurrent(int generation)
        {
            lock (_sync) return !_disposed && generation == _generation;
        }

        private void TryLighting(Action<AcerLightingClient> action)
        {
            try
            {
                lock (AcerLightingClient.Synchronization)
                {
                    using (AcerLightingClient lighting = new AcerLightingClient())
                    {
                        lighting.Connect();
                        action(lighting);
                    }
                }
            }
            catch (Exception exception)
            {
                Trace.WriteLine("SwiftControl lighting: " + exception.Message);
            }
        }

        private void DisposeTerminationTimer()
        {
            if (_terminationTimer == null) return;
            _terminationTimer.Dispose();
            _terminationTimer = null;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _generation++;
                DisposeTerminationTimer();
            }
            TryLighting(delegate(AcerLightingClient lighting)
            {
                lighting.TerminateEffect();
            });
        }
    }
}
