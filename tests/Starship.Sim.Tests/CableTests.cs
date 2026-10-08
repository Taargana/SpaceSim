using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class CableTests
{
    private static Packet P(int seq = 1) =>
        Packet.Create(new Address(10), new Address(11), PacketKind.Heartbeat, ControlTopic.None, Array.Empty<double>(), seq);

    private static (SimClock clock, NetInterface a, NetInterface b) Pair()
    {
        var clock = new SimClock(1);
        var a = new TestDevice(10).AddInterface("x");
        var b = new TestDevice(11).AddInterface("x");
        return (clock, a, b);
    }

    [Fact]
    public void Perfect_short_cable_delivers_after_latency()
    {
        var (clock, a, b) = Pair();
        var cable = TestWiring.Link(a, b);
        a.Send(P(), clock);
        cable.Step(clock);
        Assert.Empty(b.Inbox);
        TestWiring.Pump(clock, cable);
        var got = Assert.Single(b.Inbox);
        Assert.True(got.IsValid);
    }

    [Fact]
    public void Latency_grows_with_length()
    {
        Assert.Equal(1, new Cable("c", 1, 0).LatencyTicks);
        Assert.Equal(3, new Cable("c", 1, 100).LatencyTicks);
    }

    [Fact]
    public void Burnt_cable_transmits_nothing()
    {
        var (clock, a, b) = Pair();
        var cable = TestWiring.Link(a, b);
        cable.Burn();
        Assert.True(cable.IsBurnt);
        a.Send(P(), clock);
        TestWiring.Pump(clock, cable);
        Assert.Empty(b.Inbox);
    }

    [Fact]
    public void Burning_drops_packets_already_in_flight()
    {
        var (clock, a, b) = Pair();
        var cable = TestWiring.Link(a, b);
        a.Send(P(), clock);
        cable.Burn();
        TestWiring.Pump(clock, cable);
        Assert.Empty(b.Inbox);
    }

    [Fact]
    public void Detaching_drops_packets_already_in_flight_and_frees_interfaces()
    {
        var (clock, a, b) = Pair();
        var cable = TestWiring.Link(a, b);
        a.Send(P(), clock);
        cable.Detach();
        TestWiring.Pump(clock, cable);
        Assert.Empty(b.Inbox);
        Assert.Null(a.Cable);
        Assert.Null(b.Cable);
    }

    [Fact]
    public void Error_rate_is_clamped_to_one()
    {
        var c = new Cable("c", 0, 1000);
        c.Damage(0.5);
        Assert.Equal(1.0, c.ErrorRate);
    }

    [Fact]
    public void Peer_returns_the_other_end()
    {
        var (_, a, b) = Pair();
        var cable = TestWiring.Link(a, b);
        Assert.Same(b, cable.Peer(a));
        Assert.Same(a, cable.Peer(b));
    }

    private static int ValidDelivered(double quality, double length, double damage = 0)
    {
        var clock = new SimClock(42);
        var a = new TestDevice(10).AddInterface("x");
        var b = new TestDevice(11).AddInterface("x");
        var cable = TestWiring.Link(a, b, quality, length);
        cable.Damage(damage);
        for (int i = 0; i < 2000; i++) a.Send(P(i), clock);
        for (int i = 0; i < 10; i++) TestWiring.Pump(clock, cable);
        return b.Inbox.Count(p => p.IsValid);
    }

    [Fact]
    public void Zero_length_perfect_cable_loses_nothing()
    {
        Assert.Equal(2000, ValidDelivered(1.0, 0.0));
    }

    [Fact]
    public void Longer_cable_delivers_fewer_valid_packets()
    {
        Assert.True(ValidDelivered(0.5, 200) < ValidDelivered(0.5, 0));
    }

    [Fact]
    public void Lower_quality_delivers_fewer_valid_packets()
    {
        Assert.True(ValidDelivered(0.2, 10) < ValidDelivered(0.9, 10));
    }

    [Fact]
    public void Damaged_cable_delivers_fewer_valid_packets()
    {
        Assert.True(ValidDelivered(1.0, 10, damage: 0.6) < ValidDelivered(1.0, 10));
    }
}
