namespace Starship.Sim;

public sealed class Cable
{
    private sealed record InFlight(NetInterface To, Packet Packet, long ArrivalTick);

    private readonly List<InFlight> _inFlight = new();

    public Cable(string name, double quality, double length)
    {
        Name = name;
        Quality = Math.Clamp(quality, 0, 1);
        Length = Math.Max(0, length);
    }

    public string Name { get; }
    public double Quality { get; }
    public double Length { get; }
    public double Health { get; private set; } = 1.0;
    public bool IsBurnt => Health <= 0;
    public NetInterface? A { get; private set; }
    public NetInterface? B { get; private set; }

    public double ErrorRate => IsBurnt
        ? 1.0
        : Math.Clamp((1 - Quality) * 0.2 + Length * 0.002 + (1 - Health) * 0.3, 0, 1);

    public int LatencyTicks => 1 + (int)(Length / 50.0);

    public void Damage(double amount)
    {
        Health = Math.Max(0, Health - Math.Max(0, amount));
        if (IsBurnt) _inFlight.Clear();
    }

    public void Burn() => Damage(Health);

    public NetInterface Peer(NetInterface from) =>
        from == A ? B! : from == B ? A! : throw new ArgumentException("Interface is not on this cable");

    internal void Attach(NetInterface a, NetInterface b)
    {
        if (A != null) throw new InvalidOperationException($"Cable {Name} is already attached");
        A = a;
        B = b;
        a.Cable = this;
        b.Cable = this;
    }

    internal void Detach()
    {
        if (A != null) A.Cable = null;
        if (B != null) B.Cable = null;
        A = null;
        B = null;
        _inFlight.Clear();
    }

    internal void Transmit(NetInterface from, Packet packet, SimClock clock)
    {
        if (IsBurnt || A == null || B == null) return;
        var to = from == A ? B : A;
        var rate = ErrorRate;
        var rng = clock.Rng;
        if (rng.NextDouble() < rate * 0.5) return;
        if (rng.NextDouble() < rate * 0.5) packet = packet.Corrupted(rng);
        _inFlight.Add(new InFlight(to, packet, clock.Tick + LatencyTicks));
    }

    internal void Step(SimClock clock)
    {
        for (int i = 0; i < _inFlight.Count;)
        {
            var f = _inFlight[i];
            if (f.ArrivalTick <= clock.Tick)
            {
                f.To.Inbox.Enqueue(f.Packet);
                _inFlight.RemoveAt(i);
            }
            else i++;
        }
    }
}
