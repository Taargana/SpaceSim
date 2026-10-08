using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class SimRandomTests
{
    [Fact]
    public void Same_seed_gives_same_sequence()
    {
        var a = new SimRandom(7);
        var b = new SimRandom(7);
        for (int i = 0; i < 100; i++) Assert.Equal(a.NextDouble(), b.NextDouble());
    }

    [Fact]
    public void Different_seeds_give_different_sequences()
    {
        var a = new SimRandom(1);
        var b = new SimRandom(2);
        bool anyDifferent = false;
        for (int i = 0; i < 10; i++) anyDifferent |= a.NextDouble() != b.NextDouble();
        Assert.True(anyDifferent);
    }

    [Fact]
    public void NextDouble_stays_in_unit_range()
    {
        var r = new SimRandom(3);
        for (int i = 0; i < 10_000; i++)
        {
            var v = r.NextDouble();
            Assert.InRange(v, 0.0, 0.9999999999999999);
        }
    }
}
