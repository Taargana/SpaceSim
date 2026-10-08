using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class DeviceTests
{
    [Fact]
    public void Emits_heartbeat_and_telemetry_on_tick_zero()
    {
        var clock = new SimClock(1);
        var dev = new TestDevice(10);
        var iface = dev.AddInterface("x");
        var peer = new TestDevice(11).AddInterface("x");
        var cable = TestWiring.Link(iface, peer);
        dev.Emit(clock);
        TestWiring.Pump(clock, cable);
        Assert.Contains(peer.Inbox, p => p.Kind == PacketKind.Heartbeat && p.Src.Value == 10 && p.Dst.Value == 1);
        Assert.Contains(peer.Inbox, p => p.Kind == PacketKind.Telemetry);
    }

    [Fact]
    public void Same_message_goes_out_on_every_interface_with_the_same_seq()
    {
        var clock = new SimClock(1);
        var dev = new TestDevice(10);
        var i1 = dev.AddInterface("a");
        var i2 = dev.AddInterface("b");
        var p1 = new TestDevice(11).AddInterface("x");
        var p2 = new TestDevice(12).AddInterface("x");
        var c1 = TestWiring.Link(i1, p1);
        var c2 = TestWiring.Link(i2, p2);
        dev.Emit(clock);
        TestWiring.Pump(clock, c1, c2);
        var hb1 = p1.Inbox.Single(p => p.Kind == PacketKind.Heartbeat);
        var hb2 = p2.Inbox.Single(p => p.Kind == PacketKind.Heartbeat);
        Assert.Equal(hb1.Seq, hb2.Seq);
    }

    [Fact]
    public void Device_without_any_cable_emits_without_error()
    {
        var clock = new SimClock(1);
        var dev = new TestDevice(10);
        dev.AddInterface("unplugged");
        var ex = Record.Exception(() => dev.Emit(clock));
        Assert.Null(ex);
    }

    [Fact]
    public void Low_power_slows_emission()
    {
        int Count(double ratio)
        {
            var clock = new SimClock(1);
            var dev = new TestDevice(10) { };
            dev.PowerSupplyRatio = ratio;
            var iface = dev.AddInterface("x");
            var peer = new TestDevice(11).AddInterface("x");
            var cable = TestWiring.Link(iface, peer);
            for (int i = 0; i < 40; i++)
            {
                dev.Emit(clock);
                TestWiring.Pump(clock, cable);
            }
            return peer.Inbox.Count(p => p.Kind == PacketKind.Heartbeat);
        }

        Assert.Equal(4, Count(1.0));   // ticks 0, 10, 20, 30
        Assert.Equal(2, Count(0.5));   // period 20: ticks 0, 20
    }

    [Fact]
    public void Powered_flag_follows_threshold()
    {
        var dev = new TestDevice(10);
        dev.PowerSupplyRatio = 0.3;
        Assert.True(dev.Powered);
        dev.PowerSupplyRatio = 0.2;
        Assert.False(dev.Powered);
    }

    [Fact]
    public void DiscardInbound_empties_all_interfaces()
    {
        var dev = new TestDevice(10);
        var i = dev.AddInterface("x");
        i.Inbox.Enqueue(Packet.Create(new Address(1), new Address(10), PacketKind.Heartbeat, ControlTopic.None, Array.Empty<double>(), 1));
        dev.DiscardInbound();
        Assert.Empty(i.Inbox);
    }
}
