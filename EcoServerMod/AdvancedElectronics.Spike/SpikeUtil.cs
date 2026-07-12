using System;
using System.Numerics;

namespace AdvancedElectronics.Spike
{
    internal static class SpikeUtil
    {
        /// <summary>Monotonic wall-clock seconds for chat-report throttling.</summary>
        internal static double NowSeconds() => Environment.TickCount64 / 1000.0;

        internal static string Fmt(Vector3 v) => $"{v.X:F1},{v.Y:F1},{v.Z:F1}";
    }
}
