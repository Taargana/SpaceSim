namespace Starship.Sim;

public sealed class SimClock
{
    public const int TicksPerSecond = 60;
    public const double Dt = 1.0 / TicksPerSecond;

    public SimClock(int seed) => Rng = new SimRandom(seed);

    public long Tick { get; private set; }
    public SimRandom Rng { get; }

    public void Advance() => Tick++;
}
