using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class SimClockTests
{
    [Fact]
    public void Starts_at_tick_zero_and_advances()
    {
        var clock = new SimClock(1);
        Assert.Equal(0, clock.Tick);
        clock.Advance();
        clock.Advance();
        Assert.Equal(2, clock.Tick);
    }

    [Fact]
    public void Dt_is_one_sixtieth()
    {
        Assert.Equal(1.0 / 60.0, SimClock.Dt, 12);
    }
}
