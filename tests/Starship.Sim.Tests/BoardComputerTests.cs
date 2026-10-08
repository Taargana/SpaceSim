using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class BoardComputerTests
{
    private static BoardComputer Make() => new(new Address(1), RouterModels.Advanced);

    private static Packet Control(int src, ControlTopic topic, int seq, params double[] payload) =>
        Packet.Create(new Address(src), new Address(1), PacketKind.Control, topic, payload, seq);

    private static Packet Telemetry(int src, DeviceKind kind, int seq) =>
        Packet.Create(new Address(src), new Address(1), PacketKind.Telemetry, ControlTopic.None,
            new TelemetryReport(kind, 10, 20, 0.1, 1).ToPayload(), seq);

    [Fact]
    public void Applies_throttle_and_stick_commands()
    {
        var pc = Make();
        var clock = new SimClock(1);
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 0.6));
        pc.Router.LocalInbox.Enqueue(Control(11, ControlTopic.Stick, 1, 0.1, -0.2, 0.3));
        pc.Process(clock);
        Assert.Equal(0.6, pc.CommandedThrottle);
        Assert.Equal((0.1, -0.2, 0.3), pc.CommandedStick);
    }

    [Fact]
    public void Duplicate_and_older_sequence_numbers_are_ignored()
    {
        var pc = Make();
        var clock = new SimClock(1);
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 5, 0.3));
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 5, 0.9));   // redundant copy
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 4, 0.8));   // late copy
        pc.Process(clock);
        Assert.Equal(0.3, pc.CommandedThrottle);
    }

    [Fact]
    public void Out_of_range_throttle_is_clamped()
    {
        var pc = Make();
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 5.0));
        pc.Process(new SimClock(1));
        Assert.Equal(1.0, pc.CommandedThrottle);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_finite_commands_are_ignored(double value)
    {
        var pc = Make();
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 0.4));
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 2, value));
        pc.Process(new SimClock(1));
        Assert.Equal(0.4, pc.CommandedThrottle);
    }

    [Fact]
    public void Commands_with_the_wrong_payload_length_are_ignored()
    {
        var pc = Make();
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 0.1, 0.2));
        pc.Router.LocalInbox.Enqueue(Control(11, ControlTopic.Stick, 1, 0.1));
        pc.Process(new SimClock(1));
        Assert.Equal(0.0, pc.CommandedThrottle);
        Assert.Equal((0.0, 0.0, 0.0), pc.CommandedStick);
    }

    [Fact]
    public void Telemetry_updates_status_and_goes_stale()
    {
        var pc = Make();
        var clock = new SimClock(1);
        pc.Router.LocalInbox.Enqueue(Telemetry(20, DeviceKind.PowerPlant, 1));
        pc.Process(clock);
        var status = pc.Status[new Address(20)];
        Assert.Equal(DeviceKind.PowerPlant, status.Report.Kind);
        for (int i = 0; i < 60; i++) clock.Advance();
        Assert.False(pc.IsStale(new Address(20), clock));
        clock.Advance();
        Assert.True(pc.IsStale(new Address(20), clock));
        Assert.Equal(61, pc.AgeOf(new Address(20), clock));
    }

    [Fact]
    public void Unknown_device_counts_as_stale()
    {
        Assert.True(Make().IsStale(new Address(99), new SimClock(1)));
    }

    [Fact]
    public void Malformed_telemetry_is_ignored()
    {
        var pc = Make();
        var bad = Packet.Create(new Address(20), new Address(1), PacketKind.Telemetry, ControlTopic.None, new[] { 1.0, 2.0 }, 1);
        pc.Router.LocalInbox.Enqueue(bad);
        pc.Process(new SimClock(1));
        Assert.Empty(pc.Status);
    }

    private static (BoardComputer pc, SimClock clock, NetInterface propulsion, Cable cable) WithPropulsion()
    {
        var clock = new SimClock(1);
        var pc = Make();
        var iface = new TestDevice(30, DeviceKind.Propulsion).AddInterface("in");
        var cable = TestWiring.LinkPort(pc.Router.Ports[0], iface, clock);
        pc.Router.LocalInbox.Enqueue(Telemetry(30, DeviceKind.Propulsion, 1));
        return (pc, clock, iface, cable);
    }

    [Fact]
    public void Sends_a_setpoint_to_the_propulsion()
    {
        var (pc, clock, propulsion, cable) = WithPropulsion();
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 0.6));
        pc.Router.LocalInbox.Enqueue(Control(11, ControlTopic.Stick, 1, 0.1, 0.2, 0.3));
        pc.Process(clock);
        pc.Router.Step(clock);
        TestWiring.Pump(clock, cable);
        var sp = Assert.Single(propulsion.Inbox);
        Assert.Equal(ControlTopic.Setpoint, sp.Topic);
        Assert.Equal(1, sp.Src.Value);
        Assert.Equal(30, sp.Dst.Value);
        Assert.Equal(new[] { 0.6, 0.1, 0.2, 0.3 }, sp.Payload);
    }

    [Fact]
    public void No_setpoint_without_a_known_propulsion()
    {
        var clock = new SimClock(1);
        var pc = Make();
        var iface = new TestDevice(30, DeviceKind.Propulsion).AddInterface("in");
        var cable = TestWiring.LinkPort(pc.Router.Ports[0], iface, clock);
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 0.6));
        pc.Process(clock);
        pc.Router.Step(clock);
        TestWiring.Pump(clock, cable);
        Assert.Empty(iface.Inbox);
    }

    [Fact]
    public void Stale_throttle_stops_the_setpoints()
    {
        var (pc, clock, propulsion, cable) = WithPropulsion();
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 0.6));
        pc.Process(clock);                                   // tick 0: command received
        for (int i = 0; i < 32; i++) clock.Advance();        // tick 32: command is 32 ticks old
        pc.Process(clock);
        pc.Router.Step(clock);
        TestWiring.Pump(clock, cable);
        Assert.DoesNotContain(propulsion.Inbox, p => p.Topic == ControlTopic.Setpoint && p.Seq > 1);
    }

    [Fact]
    public void Stale_stick_falls_back_to_neutral_while_throttle_is_fresh()
    {
        var (pc, clock, propulsion, cable) = WithPropulsion();
        pc.Router.LocalInbox.Enqueue(Control(11, ControlTopic.Stick, 1, 1, 1, 1));
        pc.Process(clock);                                   // tick 0: stick received, no throttle yet
        for (int i = 0; i < 32; i++) clock.Advance();        // tick 32
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 0.5));
        pc.Process(clock);
        pc.Router.Step(clock);
        TestWiring.Pump(clock, cable);
        var sp = propulsion.Inbox.Last(p => p.Topic == ControlTopic.Setpoint);
        Assert.Equal(new[] { 0.5, 0.0, 0.0, 0.0 }, sp.Payload);
    }

    [Fact]
    public void Board_computer_does_not_emit_network_traffic_on_its_own()
    {
        var pc = Make();
        var ex = Record.Exception(() => pc.Emit(new SimClock(1)));
        Assert.Null(ex);
        Assert.Empty(pc.Interfaces);
    }
}
