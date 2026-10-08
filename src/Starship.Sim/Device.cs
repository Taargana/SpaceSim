namespace Starship.Sim;

public abstract class Device
{
    private readonly List<NetInterface> _interfaces = new();
    private int _seq;

    protected Device(Address address, string name, DeviceKind kind, Address target)
    {
        Address = address;
        Name = name;
        Kind = kind;
        Target = target;
    }

    public Address Address { get; internal set; }
    public string Name { get; }
    public DeviceKind Kind { get; }

    /// <summary>Address this device sends its traffic to (the board computer).</summary>
    public Address Target { get; }

    public IReadOnlyList<NetInterface> Interfaces => _interfaces;
    public double Health { get; set; } = 1.0;

    /// <summary>Electrical demand in watts.</summary>
    public double PowerDemand { get; protected set; }

    /// <summary>Fraction (0..1) of the demanded power actually supplied this tick.</summary>
    public double PowerSupplyRatio { get; internal set; } = 1.0;

    public bool Powered => PowerSupplyRatio >= SimConstants.PowerOffThreshold;

    public NetInterface AddInterface(string name)
    {
        var iface = new NetInterface(this, name);
        _interfaces.Add(iface);
        return iface;
    }

    public void DiscardInbound()
    {
        foreach (var i in _interfaces) i.Inbox.Clear();
    }

    public virtual void Emit(SimClock clock) => EmitHeartbeatAndTelemetry(clock);

    public virtual void Process(SimClock clock) { }

    /// <summary>Recompute <see cref="PowerDemand"/> from internal state; runs even when the device is switched off.</summary>
    public virtual void RefreshDemand(SimClock clock) { }

    public abstract TelemetryReport Report();

    protected int NextSeq() => ++_seq;

    /// <summary>Same packet on every interface: redundant cables carry identical copies.</summary>
    protected void Broadcast(SimClock clock, Packet packet)
    {
        foreach (var i in _interfaces) i.Send(packet, clock);
    }

    /// <summary>Under-powered devices stretch their emission period.</summary>
    protected bool IsEmitTick(SimClock clock, int basePeriod)
    {
        var period = (int)Math.Ceiling(basePeriod / Math.Max(PowerSupplyRatio, 0.01));
        return clock.Tick % period == 0;
    }

    protected void EmitHeartbeatAndTelemetry(SimClock clock)
    {
        if (IsEmitTick(clock, SimConstants.HeartbeatPeriod))
            Broadcast(clock, Packet.Create(Address, Target, PacketKind.Heartbeat, ControlTopic.None, Array.Empty<double>(), NextSeq()));
        if (IsEmitTick(clock, SimConstants.TelemetryPeriod))
            Broadcast(clock, Packet.Create(Address, Target, PacketKind.Telemetry, ControlTopic.None, Report().ToPayload(), NextSeq()));
    }
}
