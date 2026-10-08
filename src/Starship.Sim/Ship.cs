namespace Starship.Sim;

public sealed class Ship
{
    private readonly List<Device> _devices = new();
    private readonly List<Cable> _cables = new();

    public Ship(int seed, ShipBody body)
    {
        Clock = new SimClock(seed);
        Body = body;
    }

    public SimClock Clock { get; }
    public ShipBody Body { get; }
    public IReadOnlyList<Device> Devices => _devices;
    public IReadOnlyList<Cable> Cables => _cables;

    public T Add<T>(T device) where T : Device
    {
        if (_devices.Any(d => d.Address == device.Address))
            throw new ArgumentException($"Address {device.Address} is already used by another device");
        _devices.Add(device);
        return device;
    }

    public Cable Connect(NetInterface a, NetInterface b, Cable cable)
    {
        if (a.Cable != null || b.Cable != null)
            throw new InvalidOperationException("Interface is already connected");
        if (a.Owner == b.Owner)
            throw new InvalidOperationException("Cannot connect a device to itself");
        EnsureAccepted(a, b);
        EnsureAccepted(b, a);

        cable.Attach(a, b);
        if (!_cables.Contains(cable)) _cables.Add(cable);
        if (a is RouterPort pa) pa.Router.OnConnected(pa, Clock.Tick);
        if (b is RouterPort pb) pb.Router.OnConnected(pb, Clock.Tick);
        return cable;
    }

    /// <summary>Advance the whole ship by one fixed tick.</summary>
    public void Step()
    {
        foreach (var device in _devices)
        {
            if (device.Powered) device.Emit(Clock);
            else device.DiscardInbound();
        }

        foreach (var cable in _cables) cable.Step(Clock);

        var computer = _devices.OfType<BoardComputer>().FirstOrDefault();
        if (computer != null)
        {
            if (computer.Powered) computer.Router.Step(Clock);
            else computer.Router.DiscardTraffic();
        }

        foreach (var device in _devices)
            if (device.Powered) device.Process(Clock);

        foreach (var device in _devices) device.RefreshDemand(Clock);   // also for switched-off devices, so a brownout can recover
        PowerDistribution.Apply(_devices.OfType<PowerPlant>().FirstOrDefault(), _devices);

        var force = System.Numerics.Vector3.Zero;
        var torque = System.Numerics.Vector3.Zero;
        foreach (var propulsion in _devices.OfType<PropulsionSystem>())
        {
            var (f, t) = propulsion.Output(Clock);
            force += f;
            torque += t;
        }
        Body.Step(force, torque, SimClock.Dt);

        Clock.Advance();
    }

    public void Disconnect(Cable cable)
    {
        var a = cable.A ?? throw new InvalidOperationException("Cable is not connected");
        var b = cable.B!;
        cable.Detach();
        _cables.Remove(cable);
        if (a is RouterPort pa) pa.Router.OnDisconnected(pa);
        if (b is RouterPort pb) pb.Router.OnDisconnected(pb);
    }

    public Cable Replace(Cable oldCable, Cable newCable)
    {
        var a = oldCable.A ?? throw new InvalidOperationException("Cable is not connected");
        var b = oldCable.B!;
        if (newCable.A != null)
            throw new InvalidOperationException("Replacement cable is already attached");
        Disconnect(oldCable);
        return Connect(a, b, newCable);
    }

    private static void EnsureAccepted(NetInterface iface, NetInterface other)
    {
        if (iface is RouterPort port && !port.Router.CanAccept(port, other.Owner))
            throw new InvalidOperationException(
                $"Port {port.Index} of the {port.Router.Model.Name} router does not accept a {other.Owner.Kind}");
    }
}
