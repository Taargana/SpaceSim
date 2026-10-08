using Starship.Sim;

namespace Starship.Sim.Tests;

internal static class TestWiring
{
    public static Cable Link(NetInterface a, NetInterface b, double quality = 1.0, double length = 0.0)
    {
        var cable = new Cable("test", quality, length);
        cable.Attach(a, b);
        return cable;
    }

    public static Cable LinkPort(RouterPort port, NetInterface other, SimClock clock, double quality = 1.0, double length = 0.0)
    {
        var cable = Link(port, other, quality, length);
        port.Router.OnConnected(port, clock.Tick);
        return cable;
    }

    /// <summary>Advance one tick, then let the cables deliver what has arrived.</summary>
    public static void Pump(SimClock clock, params Cable[] cables)
    {
        clock.Advance();
        foreach (var c in cables) c.Step(clock);
    }
}
