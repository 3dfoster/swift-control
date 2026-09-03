using System;
using System.Threading;

namespace SwiftControl
{
    internal static class LightingNotify
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0) return 2;

            string eventName;
            if (String.Equals(args[0], "codex-complete", StringComparison.OrdinalIgnoreCase))
                eventName = LightingSignals.CodexComplete;
            else
                return 2;

            try
            {
                using (EventWaitHandle signal = EventWaitHandle.OpenExisting(eventName))
                {
                    signal.Set();
                    return 0;
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return 1;
            }
        }
    }
}
