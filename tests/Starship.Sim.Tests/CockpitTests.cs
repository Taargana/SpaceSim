using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class CockpitTests
{
    private static (SimClock clock, NetInterface peer, Cable cable, ThrottleLever throttle) Plug()
    {
        var clock = new SimClock(1);
        var throttle = new ThrottleLever(new Address(10), new Address(1));
        var peer = new TestDevice(1).AddInterface("x");
        var cable = TestWiring.Link(throttle.Out, peer);
        return (clock, peer, cable, throttle);
    }

    [Theory]
    [InlineData(0.4, 0.4)]
    [InlineData(5.0, 1.0)]
    [InlineData(-2.0, 0.0)]
    public void Throttle_position_is_clamped(double input, double expected)
    {
        var (_, _, _, throttle) = Plug();
        throttle.Set(input);
        Assert.Equal(expected, throttle.Position);
    }

    [Fact]
    public void Throttle_ignores_non_finite_input()
    {
        var (_, _, _, throttle) = Plug();
        throttle.Set(0.5);
        throttle.Set(double.NaN);
        Assert.Equal(0.5, throttle.Position);
    }

    [Fact]
    public void Throttle_emits_its_position_on_the_control_period()
    {
        var (clock, peer, cable, throttle) = Plug();
        throttle.Set(0.7);
        throttle.Emit(clock);                       // tick 0
        TestWiring.Pump(clock, cable);              // -> tick 1
        throttle.Emit(clock);                       // tick 1: no control packet
        TestWiring.Pump(clock, cable);
        var controls = peer.Inbox.Where(p => p.Kind == PacketKind.Control).ToList();
        var packet = Assert.Single(controls);
        Assert.Equal(ControlTopic.Throttle, packet.Topic);
        Assert.Equal(10, packet.Src.Value);
        Assert.Equal(1, packet.Dst.Value);
        Assert.Equal(0.7, packet.Payload[0]);
    }

    [Fact]
    public void Low_power_slows_the_control_cadence()
    {
        int Count(double ratio)
        {
            var (clock, peer, cable, throttle) = Plug();
            throttle.PowerSupplyRatio = ratio;
            for (int i = 0; i < 8; i++)
            {
                throttle.Emit(clock);
                TestWiring.Pump(clock, cable);
            }
            return peer.Inbox.Count(p => p.Kind == PacketKind.Control);
        }

        Assert.Equal(4, Count(1.0));   // ticks 0, 2, 4, 6
        Assert.Equal(2, Count(0.5));   // period 4: ticks 0, 4
    }

    [Fact]
    public void Unplugged_throttle_emits_without_error()
    {
        var throttle = new ThrottleLever(new Address(10), new Address(1));
        var ex = Record.Exception(() => throttle.Emit(new SimClock(1)));
        Assert.Null(ex);
    }

    [Fact]
    public void Joystick_clamps_and_emits_three_axes()
    {
        var clock = new SimClock(1);
        var stick = new Joystick(new Address(11), new Address(1));
        var peer = new TestDevice(1).AddInterface("x");
        var cable = TestWiring.Link(stick.Out, peer);
        stick.Set(0.5, -3.0, 2.0);
        Assert.Equal(-1.0, stick.Roll);
        Assert.Equal(1.0, stick.Yaw);
        stick.Emit(clock);
        TestWiring.Pump(clock, cable);
        var packet = peer.Inbox.Single(p => p.Kind == PacketKind.Control);
        Assert.Equal(ControlTopic.Stick, packet.Topic);
        Assert.Equal(new[] { 0.5, -1.0, 1.0 }, packet.Payload);
    }

    [Fact]
    public void Joystick_ignores_non_finite_input()
    {
        var stick = new Joystick(new Address(11), new Address(1));
        stick.Set(0.2, 0.2, 0.2);
        stick.Set(double.NaN, 0.9, 0.9);
        Assert.Equal(0.2, stick.Pitch);
        Assert.Equal(0.2, stick.Roll);
    }
}
