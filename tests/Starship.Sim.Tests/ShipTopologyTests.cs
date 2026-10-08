using System.Numerics;
using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class ShipTopologyTests
{
    private static (Ship ship, Router router) Make(RouterModel model)
    {
        var ship = new Ship(1, new ShipBody(1000, Vector3.One));
        var owner = ship.Add(new TestDevice(1, DeviceKind.BoardComputer));
        return (ship, new Router(owner, model));
    }

    [Fact]
    public void Connect_attaches_the_cable_and_binds_the_router_port()
    {
        var (ship, router) = Make(RouterModels.Basic);
        var throttle = ship.Add(new TestDevice(10, DeviceKind.Throttle));
        var iface = throttle.AddInterface("out");
        var cable = ship.Connect(router.Ports[0], iface, new Cable("c", 1, 2));
        Assert.Same(cable, iface.Cable);
        Assert.Same(cable, router.Ports[0].Cable);
        Assert.Equal(new Address(10), router.Ports[0].BoundAddress);
        Assert.Contains(cable, ship.Cables);
    }

    [Fact]
    public void Dedicated_port_refuses_the_wrong_device_kind()
    {
        var (ship, router) = Make(RouterModels.Basic);
        var joystick = ship.Add(new TestDevice(11, DeviceKind.Joystick));
        var iface = joystick.AddInterface("out");
        Assert.Throws<InvalidOperationException>(() => ship.Connect(router.Ports[0], iface, new Cable("c", 1, 2)));
        Assert.Null(iface.Cable);
        Assert.Null(router.Ports[0].BoundAddress);
    }

    [Fact]
    public void Connecting_an_already_connected_interface_is_refused()
    {
        var (ship, router) = Make(RouterModels.Advanced);
        var dev = ship.Add(new TestDevice(10));
        var iface = dev.AddInterface("out");
        ship.Connect(router.Ports[0], iface, new Cable("a", 1, 1));
        Assert.Throws<InvalidOperationException>(() => ship.Connect(router.Ports[1], iface, new Cable("b", 1, 1)));
    }

    [Fact]
    public void Connecting_two_interfaces_of_the_same_device_is_refused()
    {
        var (ship, _) = Make(RouterModels.Advanced);
        var dev = ship.Add(new TestDevice(10));
        var a = dev.AddInterface("a");
        var b = dev.AddInterface("b");
        Assert.Throws<InvalidOperationException>(() => ship.Connect(a, b, new Cable("loop", 1, 1)));
    }

    [Fact]
    public void Disconnect_frees_both_ends()
    {
        var (ship, router) = Make(RouterModels.Advanced);
        var dev = ship.Add(new TestDevice(10));
        var iface = dev.AddInterface("out");
        var cable = ship.Connect(router.Ports[0], iface, new Cable("c", 1, 1));
        ship.Disconnect(cable);
        Assert.Null(iface.Cable);
        Assert.Null(router.Ports[0].Cable);
        Assert.Null(router.Ports[0].BoundAddress);
        Assert.DoesNotContain(cable, ship.Cables);
    }

    [Fact]
    public void Replace_swaps_the_cable_and_gives_the_new_route_a_fresh_grace_period()
    {
        var (ship, router) = Make(RouterModels.Advanced);
        var dev = ship.Add(new TestDevice(10));
        var iface = dev.AddInterface("out");
        var old = ship.Connect(router.Ports[0], iface, new Cable("old", 1, 1));
        old.Burn();
        for (int i = 0; i < 100; i++) ship.Clock.Advance();
        var fresh = ship.Replace(old, new Cable("new", 1, 1));
        Assert.Same(fresh, iface.Cable);
        Assert.Null(old.A);
        Assert.Equal(ship.Clock.Tick, router.Ports[0].LastRxTick);
        Assert.True(router.IsUp(router.Ports[0], ship.Clock));
    }

    [Fact]
    public void Replace_with_an_already_attached_cable_throws_and_leaves_the_old_cable_connected()
    {
        var (ship, router) = Make(RouterModels.Advanced);
        var iface = ship.Add(new TestDevice(10)).AddInterface("out");
        var other = ship.Add(new TestDevice(11)).AddInterface("out");
        var old = ship.Connect(router.Ports[0], iface, new Cable("old", 1, 1));
        var used = ship.Connect(router.Ports[1], other, new Cable("used", 1, 1));
        Assert.Throws<InvalidOperationException>(() => ship.Replace(old, used));
        Assert.Same(old, iface.Cable);
        Assert.NotNull(old.A);
        Assert.Contains(old, ship.Cables);
        Assert.Equal(new Address(10), router.Ports[0].BoundAddress);
    }

    [Fact]
    public void Duplicate_addresses_are_refused()
    {
        var (ship, _) = Make(RouterModels.Basic);
        ship.Add(new TestDevice(10));
        Assert.Throws<ArgumentException>(() => ship.Add(new TestDevice(10)));
    }
}
