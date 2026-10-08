using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class RouterTests
{
    private static (SimClock clock, TestDevice owner, Router router) Make(RouterModel model)
    {
        var clock = new SimClock(1);
        var owner = new TestDevice(1, DeviceKind.BoardComputer);
        return (clock, owner, new Router(owner, model));
    }

    private static Packet Pkt(int src, int dst, int seq = 1) =>
        Packet.Create(new Address(src), new Address(dst), PacketKind.Control, ControlTopic.Throttle, new[] { 0.5 }, seq);

    private static RouterModel Custom(int ports, RouterCapabilities caps, int throughput = 64, int queue = 64) =>
        new("Custom", Enumerable.Repeat(new PortSpec(null), ports).ToArray(), caps, throughput, queue);

    [Fact]
    public void Gammes_have_expected_port_counts_and_capabilities()
    {
        Assert.Equal(4, RouterModels.Basic.Ports.Count);
        Assert.False(RouterModels.Basic.Capabilities.Failover);
        Assert.Equal(8, RouterModels.Intermediate.Ports.Count);
        Assert.True(RouterModels.Intermediate.Capabilities.Failover);
        Assert.Equal(16, RouterModels.Advanced.Ports.Count);
        Assert.True(RouterModels.Advanced.Capabilities.ConfigurableAddressing);
    }

    [Fact]
    public void Forwards_packet_to_the_port_bound_to_its_destination()
    {
        var (clock, _, router) = Make(RouterModels.Advanced);
        var ia = new TestDevice(10).AddInterface("x");
        var ib = new TestDevice(11).AddInterface("x");
        var ca = TestWiring.LinkPort(router.Ports[0], ia, clock);
        var cb = TestWiring.LinkPort(router.Ports[1], ib, clock);
        router.Ports[0].Inbox.Enqueue(Pkt(10, 11));
        router.Step(clock);
        TestWiring.Pump(clock, ca, cb);
        var got = Assert.Single(ib.Inbox);
        Assert.Equal(11, got.Dst.Value);
        Assert.Empty(ia.Inbox);
    }

    [Fact]
    public void Delivers_packets_for_the_owner_locally()
    {
        var (clock, _, router) = Make(RouterModels.Advanced);
        router.Ports[0].Inbox.Enqueue(Pkt(10, 1));
        router.Step(clock);
        Assert.Single(router.LocalInbox);
    }

    [Fact]
    public void Drops_corrupted_packets()
    {
        var (clock, _, router) = Make(RouterModels.Advanced);
        router.Ports[0].Inbox.Enqueue(Pkt(10, 1).Corrupted(clock.Rng));
        router.Step(clock);
        Assert.Empty(router.LocalInbox);
        Assert.Equal(1, router.DroppedCount);
    }

    [Fact]
    public void Drops_and_counts_packets_for_unknown_destinations()
    {
        var (clock, _, router) = Make(RouterModels.Advanced);
        router.Ports[0].Inbox.Enqueue(Pkt(10, 99));
        var ex = Record.Exception(() => router.Step(clock));
        Assert.Null(ex);
        Assert.Equal(1, router.DroppedCount);
    }

    [Fact]
    public void Throughput_limits_packets_processed_per_tick()
    {
        var (clock, _, router) = Make(Custom(1, new RouterCapabilities(false, false, false, 1), throughput: 2));
        for (int i = 1; i <= 5; i++) router.Submit(Pkt(10, 1, i));
        router.Step(clock);
        Assert.Equal(2, router.LocalInbox.Count);
        router.Step(clock);
        Assert.Equal(4, router.LocalInbox.Count);
    }

    [Fact]
    public void Full_queue_drops_overflow()
    {
        var (_, _, router) = Make(Custom(1, new RouterCapabilities(false, false, false, 1), queue: 3));
        for (int i = 1; i <= 5; i++) router.Submit(Pkt(10, 1, i));
        Assert.Equal(2, router.DroppedCount);
    }

    [Fact]
    public void Dedicated_port_accepts_only_its_device_kind()
    {
        var (_, _, router) = Make(RouterModels.Basic);
        Assert.True(router.CanAccept(router.Ports[0], new TestDevice(10, DeviceKind.Throttle)));
        Assert.False(router.CanAccept(router.Ports[0], new TestDevice(11, DeviceKind.Joystick)));
    }

    [Fact]
    public void FindFreePort_prefers_dedicated_then_generic()
    {
        var (clock, _, router) = Make(RouterModels.Intermediate);
        var first = router.FindFreePort(DeviceKind.Propulsion);
        Assert.Equal(2, first!.Index);
        TestWiring.LinkPort(first, new TestDevice(30, DeviceKind.Propulsion).AddInterface("a"), clock);
        var second = router.FindFreePort(DeviceKind.Propulsion);
        Assert.Equal(4, second!.Index);
    }

    private static (SimClock clock, Router router, NetInterface i1, NetInterface i2, Cable c1, Cable c2) Redundant(RouterModel model)
    {
        var (clock, _, router) = Make(model);
        var dev = new TestDevice(30, DeviceKind.Propulsion);
        var i1 = dev.AddInterface("a");
        var i2 = dev.AddInterface("b");
        var c1 = TestWiring.LinkPort(router.Ports[2], i1, clock);
        var c2 = TestWiring.LinkPort(router.Ports[4], i2, clock);
        return (clock, router, i1, i2, c1, c2);
    }

    [Fact]
    public void With_failover_a_silent_route_is_replaced_by_a_live_one()
    {
        var (clock, router, i1, i2, c1, c2) = Redundant(RouterModels.Intermediate);
        for (int i = 0; i < 100; i++) clock.Advance();
        router.Ports[4].LastRxTick = clock.Tick;   // port 2 stays silent since tick 0
        router.Submit(Pkt(1, 30));
        router.Step(clock);
        TestWiring.Pump(clock, c1, c2);
        Assert.Empty(i1.Inbox);
        Assert.Single(i2.Inbox);
    }

    [Fact]
    public void With_failover_the_first_route_is_used_while_it_is_alive()
    {
        var (clock, router, i1, i2, c1, c2) = Redundant(RouterModels.Intermediate);
        router.Submit(Pkt(1, 30));
        router.Step(clock);
        TestWiring.Pump(clock, c1, c2);
        Assert.Single(i1.Inbox);
        Assert.Empty(i2.Inbox);
    }

    [Fact]
    public void Without_failover_the_first_route_is_kept_even_if_silent()
    {
        var model = Custom(8, new RouterCapabilities(false, false, false, 2));
        var clock = new SimClock(1);
        var router = new Router(new TestDevice(1, DeviceKind.BoardComputer), model);
        var dev = new TestDevice(30, DeviceKind.Propulsion);
        var i1 = dev.AddInterface("a");
        var i2 = dev.AddInterface("b");
        var c1 = TestWiring.LinkPort(router.Ports[0], i1, clock);
        var c2 = TestWiring.LinkPort(router.Ports[1], i2, clock);
        for (int i = 0; i < 100; i++) clock.Advance();
        router.Ports[1].LastRxTick = clock.Tick;
        router.Submit(Pkt(1, 30));
        router.Step(clock);
        TestWiring.Pump(clock, c1, c2);
        Assert.Single(i1.Inbox);
        Assert.Empty(i2.Inbox);
    }

    [Fact]
    public void Receiving_from_the_bound_address_refreshes_the_port()
    {
        var (clock, _, router) = Make(RouterModels.Advanced);
        TestWiring.LinkPort(router.Ports[0], new TestDevice(10).AddInterface("x"), clock);
        for (int i = 0; i < 50; i++) clock.Advance();
        Assert.False(router.IsUp(router.Ports[0], clock));
        router.Ports[0].Inbox.Enqueue(Pkt(10, 1));
        router.Step(clock);
        Assert.True(router.IsUp(router.Ports[0], clock));
    }

    [Fact]
    public void SetRoute_requires_the_capability()
    {
        var (_, _, basic) = Make(RouterModels.Basic);
        Assert.Throws<InvalidOperationException>(() => basic.SetRoute(new Address(30), basic.Ports[0]));
    }

    [Fact]
    public void Explicit_route_takes_priority_over_automatic_order()
    {
        var (clock, router, i1, i2, c1, c2) = Redundant(RouterModels.Intermediate);
        router.SetRoute(new Address(30), router.Ports[4], router.Ports[2]);
        router.Submit(Pkt(1, 30));
        router.Step(clock);
        TestWiring.Pump(clock, c1, c2);
        Assert.Empty(i1.Inbox);
        Assert.Single(i2.Inbox);
    }

    [Fact]
    public void SetRoute_rejects_more_routes_than_the_model_allows()
    {
        var (_, _, router) = Make(RouterModels.Intermediate);
        Assert.Throws<ArgumentException>(() =>
            router.SetRoute(new Address(30), router.Ports[2], router.Ports[4], router.Ports[5]));
    }

    [Fact]
    public void AssignAddress_requires_the_capability()
    {
        var (clock, _, basic) = Make(RouterModels.Basic);
        TestWiring.LinkPort(basic.Ports[0], new TestDevice(10).AddInterface("x"), clock);
        Assert.Throws<InvalidOperationException>(() => basic.AssignAddress(basic.Ports[0], new Address(77)));
    }

    [Fact]
    public void AssignAddress_rebinds_every_port_of_the_device()
    {
        var (clock, _, router) = Make(RouterModels.Advanced);
        var dev = new TestDevice(10);
        TestWiring.LinkPort(router.Ports[0], dev.AddInterface("a"), clock);
        TestWiring.LinkPort(router.Ports[1], dev.AddInterface("b"), clock);
        router.AssignAddress(router.Ports[0], new Address(77));
        Assert.Equal(77, dev.Address.Value);
        Assert.Equal(new Address(77), router.Ports[0].BoundAddress);
        Assert.Equal(new Address(77), router.Ports[1].BoundAddress);
    }

    [Fact]
    public void AssignAddress_needs_a_connected_port()
    {
        var (_, _, router) = Make(RouterModels.Advanced);
        Assert.Throws<InvalidOperationException>(() => router.AssignAddress(router.Ports[0], new Address(77)));
    }

    [Fact]
    public void DiscardTraffic_clears_everything_pending()
    {
        var (clock, _, router) = Make(RouterModels.Advanced);
        router.Ports[0].Inbox.Enqueue(Pkt(10, 1));
        router.Submit(Pkt(10, 1, 2));
        router.Step(clock);
        router.Ports[0].Inbox.Enqueue(Pkt(10, 1, 3));
        router.DiscardTraffic();
        Assert.Empty(router.LocalInbox);
        Assert.Empty(router.Ports[0].Inbox);
        router.Step(clock);
        Assert.Empty(router.LocalInbox);
    }
}
