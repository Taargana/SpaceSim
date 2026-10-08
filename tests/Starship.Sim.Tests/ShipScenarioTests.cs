using System.Numerics;
using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class ShipScenarioTests
{
    private static void Run(DefaultShip s, int ticks)
    {
        for (int i = 0; i < ticks; i++) s.Ship.Step();
    }

    [Fact]
    public void Full_throttle_accelerates_the_ship_forward()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic);
        s.Throttle.Set(1.0);
        Run(s, 600);
        Assert.InRange(s.Ship.Body.Velocity.Z, 100f, 200f);
        Assert.InRange(Math.Abs(s.Ship.Body.Velocity.X), 0f, 1e-3f);
        Assert.True(s.Ship.Body.Position.Z > 0);
    }

    [Fact]
    public void Telemetry_of_every_device_reaches_the_computer()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic);
        Run(s, 120);
        foreach (var address in new[] { 10, 11, 20, 30 })
        {
            Assert.True(s.Computer.Status.ContainsKey(new Address(address)));
            Assert.False(s.Computer.IsStale(new Address(address), s.Ship.Clock));
        }
    }

    [Fact]
    public void Without_throttle_the_ship_stays_put()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic);
        Run(s, 300);
        Assert.Equal(Vector3.Zero, s.Ship.Body.Velocity);
    }

    [Fact]
    public void Stick_input_rotates_the_ship()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic);
        s.Joystick.Set(1, 0, 0);
        Run(s, 120);
        Assert.True(s.Ship.Body.AngularVelocity.X > 0.5f);
        Assert.InRange(s.Ship.Body.Velocity.Length(), 0f, 1e-3f);
    }

    [Fact]
    public void Same_seed_gives_identical_runs_with_noisy_cables()
    {
        (Vector3 pos, Vector3 vel, Quaternion rot, int dropped) Simulate(int seed)
        {
            var s = ShipBuilder.CreateDefault(seed, RouterModels.Basic, cableQuality: 0.8);
            s.Throttle.Set(0.8);
            s.Joystick.Set(0.3, 0, 0.2);
            Run(s, 600);
            return (s.Ship.Body.Position, s.Ship.Body.Velocity, s.Ship.Body.Orientation, s.Computer.Router.DroppedCount);
        }

        var first = Simulate(7);
        var second = Simulate(7);
        Assert.Equal(first, second);
        Assert.True(first.dropped > 0, "noisy cables should corrupt some packets");
    }

    [Fact]
    public void Burnt_throttle_cable_without_redundancy_cuts_the_thrust_but_not_the_momentum()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic);
        s.Throttle.Set(1.0);
        Run(s, 120);
        s.Cables["throttle"].Burn();
        Run(s, 100);                                   // control timeout + setpoint hold elapsed
        var v1 = s.Ship.Body.Velocity;
        var p1 = s.Ship.Body.Position;
        Run(s, 60);
        Assert.True(v1.Z > 10f);
        Assert.Equal(v1, s.Ship.Body.Velocity);        // no more thrust
        Assert.True(s.Ship.Body.Position.Z > p1.Z);    // still drifting
    }

    [Fact]
    public void Replacing_the_burnt_cable_restores_control()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic);
        s.Throttle.Set(1.0);
        Run(s, 60);
        s.Cables["throttle"].Burn();
        Run(s, 100);
        var v1 = s.Ship.Body.Velocity.Z;
        s.Ship.Replace(s.Cables["throttle"], new Cable("throttle2", 1.0, 4));
        Run(s, 120);
        Assert.True(s.Ship.Body.Velocity.Z > v1 + 10f);
    }

    [Fact]
    public void Redundant_propulsion_cable_keeps_the_thrust_after_a_failure()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Intermediate);
        var port = s.Computer.Router.FindFreePort(DeviceKind.Propulsion)!;
        s.Ship.Connect(port, s.Propulsion.AddInterface("backup"), new Cable("backup", 1.0, 3));

        s.Throttle.Set(1.0);
        Run(s, 120);
        s.Cables["propulsion"].Burn();
        Run(s, 200);                                   // failover detection + settling
        var v1 = s.Ship.Body.Velocity.Z;
        Run(s, 100);
        Assert.True(s.Ship.Body.Velocity.Z - v1 > 25f, "thrust should continue through the backup route");
    }

    [Fact]
    public void Without_a_backup_cable_a_propulsion_cable_failure_cuts_the_thrust()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Intermediate);
        s.Throttle.Set(1.0);
        Run(s, 120);
        s.Cables["propulsion"].Burn();
        Run(s, 200);
        var v1 = s.Ship.Body.Velocity;
        Run(s, 100);
        Assert.Equal(v1, s.Ship.Body.Velocity);
    }

    [Fact]
    public void Power_outage_stops_the_thrust_and_recovery_restores_it_without_stale_replay()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic);
        s.Throttle.Set(1.0);
        Run(s, 120);
        s.Plant.Online = false;
        Run(s, 60);
        var v1 = s.Ship.Body.Velocity;
        Run(s, 60);
        Assert.Equal(v1, s.Ship.Body.Velocity);
        Assert.Empty(s.Propulsion.In.Inbox);
        Assert.All(s.Computer.Router.Ports, p => Assert.Empty(p.Inbox));

        s.Plant.Online = true;
        Run(s, 120);
        Assert.True(s.Ship.Body.Velocity.Z > v1.Z + 10f);
    }

    [Fact]
    public void Default_ship_is_fully_wired()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Advanced);
        Assert.Equal(4, s.Cables.Count);
        Assert.Equal(5, s.Ship.Devices.Count);
        Assert.All(s.Ship.Cables, c => Assert.NotNull(c.A));
    }

    [Fact]
    public void Brownout_reduces_thrust_but_the_ship_keeps_flying()
    {
        var full = ShipBuilder.CreateDefault(1, RouterModels.Basic, plantCapacity: 1000);
        var weak = ShipBuilder.CreateDefault(1, RouterModels.Basic, plantCapacity: 150);   // demand ~260 W -> ratio ~0.58
        full.Throttle.Set(1.0);
        weak.Throttle.Set(1.0);
        Run(full, 600);
        Run(weak, 600);
        Assert.True(weak.Propulsion.PowerSupplyRatio < 1.0);
        Assert.True(weak.Propulsion.Powered);
        Assert.InRange(weak.Ship.Body.Velocity.Z, 20f, full.Ship.Body.Velocity.Z - 20f);
    }

    [Fact]
    public void Reassigning_the_propulsion_address_does_not_ground_the_ship()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Advanced);
        s.Throttle.Set(1.0);
        Run(s, 120);
        var port = (RouterPort)s.Propulsion.In.Cable!.Peer(s.Propulsion.In);
        s.Computer.Router.AssignAddress(port, new Address(31));
        Run(s, 200);                                   // new telemetry source learned, old one goes stale
        var v1 = s.Ship.Body.Velocity.Z;
        Run(s, 100);
        Assert.True(s.Ship.Body.Velocity.Z - v1 > 25f, "setpoints must follow the propulsion to its new address");
    }

    [Fact]
    public void Brownout_below_the_shutdown_threshold_recovers_once_the_throttle_is_released()
    {
        // 60 W cannot sustain the 260 W full-throttle demand (ratio < 0.25 switches devices off).
        // Once the throttle is released the propulsion demand must fall back to idle even though
        // it is switched off and no longer runs Process, so power (and the computer) come back.
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic, plantCapacity: 60);
        s.Throttle.Set(1.0);
        Run(s, 300);
        s.Throttle.Set(0.0);
        Run(s, 600);
        Assert.True(s.Propulsion.PowerSupplyRatio >= 0.25, $"ratio stayed {s.Propulsion.PowerSupplyRatio}");
        Assert.True(s.Computer.Powered);

        // Full throttle cannot be sustained with 60 W, so only assert that the ship is still
        // powered and its velocity never decreases (there is no braking) after commanding thrust again.
        var v1 = s.Ship.Body.Velocity.Z;
        s.Throttle.Set(1.0);
        Run(s, 300);
        Assert.True(s.Ship.Body.Velocity.Z >= v1);
        Assert.True(s.Computer.Powered || s.Propulsion.PowerSupplyRatio > 0);
    }
}
