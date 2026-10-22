namespace RR.Sim.Core
{
    /// Fixed 60 Hz clock. All sim time is integer ticks; milliseconds only appear
    /// in data and are converted once, rounding half up.
    public static class SimTime
    {
        public const int TicksPerSecond = 60;

        public static int MsToTicks(int ms)
        {
            if (ms <= 0) return 0;
            return (int)(((long)ms * TicksPerSecond + 500) / 1000);
        }

        public static int TicksToMs(int ticks) => (int)((long)ticks * 1000 / TicksPerSecond);

        public static double TicksToSeconds(int ticks) => ticks / (double)TicksPerSecond;
    }
}
