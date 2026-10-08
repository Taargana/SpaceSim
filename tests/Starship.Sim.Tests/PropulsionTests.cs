using System.Numerics;
using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class PropulsionTests
{
    private static PropulsionSystem Make() =>
        new(new Address(30), new Address(1), 1000, new Vector3(100, 200, 300));

    private static Packet Setpoint(int seq, params double[] payload) =>
        Packet.Create(new Address(1), new Address(30), PacketKind.Control, ControlTopic.Setpoint, payload, seq);

    private static void Feed(PropulsionSystem p, Packet packet, SimClock clock)
    {
        p.In.Inbox.Enqueue(packet);
        p.Process(clock);
    }

    [Fact]
    public void Thrust_acts_along_forward_axis_scaled_by_throttle()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 0.5, 0, 0, 0), clock);
        var (force, torque) = p.Output(clock);
        Assert.Equal(new Vector3(0, 0, 500), force);
        Assert.Equal(Vector3.Zero, torque);
    }

    [Fact]
    public void Stick_maps_pitch_to_x_yaw_to_y_roll_to_z()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 0, 0.5, 1, -1), clock);   // pitch .5, roll 1, yaw -1
        var (_, torque) = p.Output(clock);
        Assert.Equal(new Vector3(50, -200, 300), torque);
    }

    [Fact]
    public void Setpoint_is_held_then_cut()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 1, 0, 0, 0), clock);       // tick 0
        for (int i = 0; i < 30; i++) clock.Advance();
        Assert.NotEqual(Vector3.Zero, p.Output(clock).Force);    // tick 30: still held
        clock.Advance();
        Assert.Equal(Vector3.Zero, p.Output(clock).Force);       // tick 31: cut
    }

    [Fact]
    public void Without_any_setpoint_there_is_no_thrust()
    {
        var clock = new SimClock(1);
        Assert.Equal((Vector3.Zero, Vector3.Zero), Make().Output(clock));
    }

    [Fact]
    public void Power_ratio_scales_output_and_low_power_cuts_it()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 1, 0, 0, 0), clock);
        p.PowerSupplyRatio = 0.5;
        Assert.Equal(new Vector3(0, 0, 500), p.Output(clock).Force);
        p.PowerSupplyRatio = 0.2;
        Assert.Equal(Vector3.Zero, p.Output(clock).Force);
    }

    [Fact]
    public void Corrupted_packets_are_ignored()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 1, 0, 0, 0).Corrupted(clock.Rng), clock);
        Assert.Equal(Vector3.Zero, p.Output(clock).Force);
    }

    [Fact]
    public void Packets_for_someone_else_or_other_topics_are_ignored()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Packet.Create(new Address(1), new Address(99), PacketKind.Control, ControlTopic.Setpoint, new[] { 1.0, 0, 0, 0 }, 1), clock);
        Feed(p, Packet.Create(new Address(1), new Address(30), PacketKind.Control, ControlTopic.Throttle, new[] { 1.0, 0, 0, 0 }, 2), clock);
        Assert.Equal(Vector3.Zero, p.Output(clock).Force);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_setpoints_are_ignored(double bad)
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 0.4, 0, 0, 0), clock);
        Feed(p, Setpoint(2, bad, 0, 0, 0), clock);
        Assert.Equal(new Vector3(0, 0, 400), p.Output(clock).Force);
    }

    [Fact]
    public void Wrong_length_setpoints_are_ignored()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 1, 0), clock);
        Assert.Equal(Vector3.Zero, p.Output(clock).Force);
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 9, 9, -9, 0), clock);
        var (force, torque) = p.Output(clock);
        Assert.Equal(new Vector3(0, 0, 1000), force);
        Assert.Equal(new Vector3(100, 0, -300), torque);
    }

    [Fact]
    public void Older_sequence_numbers_are_ignored()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(5, 0.5, 0, 0, 0), clock);
        Feed(p, Setpoint(4, 1.0, 0, 0, 0), clock);
        Assert.Equal(new Vector3(0, 0, 500), p.Output(clock).Force);
    }

    [Fact]
    public void Power_demand_rises_with_throttle()
    {
        var clock = new SimClock(1);
        var p = Make();
        p.Process(clock);
        Assert.Equal(20, p.PowerDemand);
        Feed(p, Setpoint(1, 1, 0, 0, 0), clock);
        Assert.Equal(220, p.PowerDemand);
    }

    [Fact]
    public void Setpoints_are_accepted_from_any_interface()
    {
        var clock = new SimClock(1);
        var p = Make();
        var second = p.AddInterface("backup");
        second.Inbox.Enqueue(Setpoint(1, 1, 0, 0, 0));
        p.Process(clock);
        Assert.Equal(new Vector3(0, 0, 1000), p.Output(clock).Force);
    }

    [Fact]
    public void RefreshDemand_falls_back_to_idle_after_the_hold_expires_without_Process()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 1, 0, 0, 0), clock);
        Assert.Equal(220, p.PowerDemand);
        for (int i = 0; i < SimConstants.SetpointHoldTicks + 1; i++) clock.Advance();
        p.RefreshDemand(clock);
        Assert.Equal(20, p.PowerDemand);
    }

    [Fact]
    public void Telemetry_reports_zero_load_once_the_setpoint_has_expired()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 1, 0, 0, 0), clock);
        Assert.Equal(1.0, p.Report().Load, 9);
        for (int i = 0; i < SimConstants.SetpointHoldTicks + 1; i++) clock.Advance();
        p.RefreshDemand(clock);
        Assert.Equal(0.0, p.Report().Load, 9);
    }
}
