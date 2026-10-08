namespace Starship.Sim;

public sealed class Router
{
    private readonly Device _owner;
    private readonly List<RouterPort> _ports;
    private readonly Queue<Packet> _pending = new();
    private readonly Dictionary<Address, List<RouterPort>> _explicitRoutes = new();

    public Router(Device owner, RouterModel model)
    {
        _owner = owner;
        Model = model;
        _ports = model.Ports.Select((spec, i) => new RouterPort(this, owner, i, spec)).ToList();
    }

    public RouterModel Model { get; }
    public IReadOnlyList<RouterPort> Ports => _ports;

    /// <summary>Packets addressed to the owning device (the board computer).</summary>
    public Queue<Packet> LocalInbox { get; } = new();

    public int DroppedCount { get; private set; }

    /// <summary>Entry point for packets emitted by the owner.</summary>
    public void Submit(Packet packet) => Enqueue(packet);

    public void Step(SimClock clock)
    {
        foreach (var port in _ports)
        {
            while (port.Inbox.TryDequeue(out var packet))
            {
                if (!packet.IsValid)
                {
                    DroppedCount++;
                    continue;
                }
                if (port.BoundAddress == packet.Src) port.LastRxTick = clock.Tick;
                Enqueue(packet);
            }
        }

        for (int budget = Model.Throughput; budget > 0 && _pending.TryDequeue(out var next); budget--)
            Forward(next, clock);
    }

    public bool CanAccept(RouterPort port, Device device) =>
        port.Spec.Dedicated == null || port.Spec.Dedicated == device.Kind;

    public bool IsUp(RouterPort port, SimClock clock) =>
        port.IsConnected && clock.Tick - port.LastRxTick <= SimConstants.HeartbeatTimeoutTicks;

    public RouterPort? FindFreePort(DeviceKind kind) =>
        _ports.FirstOrDefault(p => !p.IsConnected && p.Spec.Dedicated == kind)
        ?? _ports.FirstOrDefault(p => !p.IsConnected && p.Spec.Dedicated == null);

    public void SetRoute(Address dst, params RouterPort[] ports)
    {
        if (!Model.Capabilities.RoutingTable)
            throw new InvalidOperationException($"{Model.Name} router has no routing table");
        if (ports.Length == 0 || ports.Length > Model.Capabilities.MaxRoutesPerDst)
            throw new ArgumentException($"{Model.Name} router accepts 1 to {Model.Capabilities.MaxRoutesPerDst} routes per destination");
        _explicitRoutes[dst] = ports.ToList();
    }

    public void AssignAddress(RouterPort port, Address address)
    {
        if (!Model.Capabilities.ConfigurableAddressing)
            throw new InvalidOperationException($"{Model.Name} router does not allow address assignment");
        if (port.Cable == null)
            throw new InvalidOperationException("Port is not connected");
        var peer = port.Cable.Peer(port).Owner;
        if (address == _owner.Address)
            throw new InvalidOperationException($"Address {address} belongs to the router owner");
        foreach (var p in _ports)
            if (p.Cable != null && p.Cable.Peer(p).Owner is var other && other != peer && other.Address == address)
                throw new InvalidOperationException($"Address {address} is already used by a device connected to the router");

        var old = peer.Address;
        if (old != address && _explicitRoutes.Remove(old, out var routes))
            _explicitRoutes[address] = routes;
        peer.Address = address;
        foreach (var p in _ports)
            if (p.Cable != null && p.Cable.Peer(p).Owner == peer) p.BoundAddress = address;
    }

    public void DiscardTraffic()
    {
        foreach (var p in _ports) p.Inbox.Clear();
        _pending.Clear();
        LocalInbox.Clear();
    }

    internal void OnConnected(RouterPort port, long tick)
    {
        port.BoundAddress = port.Cable!.Peer(port).Owner.Address;
        port.LastRxTick = tick;
    }

    internal void OnDisconnected(RouterPort port) => port.BoundAddress = null;

    private void Enqueue(Packet packet)
    {
        if (_pending.Count >= Model.QueueCapacity)
        {
            DroppedCount++;
            return;
        }
        _pending.Enqueue(packet);
    }

    private void Forward(Packet packet, SimClock clock)
    {
        if (packet.Dst == _owner.Address)
        {
            LocalInbox.Enqueue(packet);
            return;
        }
        var port = SelectRoute(packet.Dst, clock);
        if (port == null)
        {
            DroppedCount++;
            return;
        }
        port.Send(packet, clock);
    }

    private RouterPort? SelectRoute(Address dst, SimClock clock)
    {
        var candidates = Candidates(dst);
        if (candidates.Count == 0) return null;
        if (!Model.Capabilities.Failover) return candidates[0];
        return candidates.FirstOrDefault(p => IsUp(p, clock)) ?? candidates[0];
    }

    private List<RouterPort> Candidates(Address dst)
    {
        if (_explicitRoutes.TryGetValue(dst, out var explicitRoute))
            return explicitRoute.Where(p => p.IsConnected).ToList();
        return _ports
            .Where(p => p.IsConnected && p.BoundAddress == dst)
            .Take(Model.Capabilities.MaxRoutesPerDst)
            .ToList();
    }
}
