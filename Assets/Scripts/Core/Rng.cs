namespace FlickCurling.Core
{
    /// <summary>
    /// The match's random numbers (the turn order shot's target, its coin toss): SplitMix64, integer arithmetic only, so
    /// the same seed gives the same numbers on every device and runtime, and its whole state is one number that a saved
    /// match (MatchState) carries along.
    /// </summary>
    public sealed class Rng
    {
        public ulong State;

        public Rng(ulong seed) { State = seed; }

        public ulong NextULong()
        {
            ulong z = State += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform in [0, 1), 53 bits.</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));
    }
}
