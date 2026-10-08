using System.Numerics;

namespace Starship.Sim;

public sealed class PropulsionSystem : Device
{
    private const double BaseDemand = 20;
    private const double DemandPerThrottle = 200;

    private double _throttle;
    private Vector3 _stick;                       // x pitch, y yaw, z roll, each in [-1, 1]
    private long _setpointTick = long.MinValue / 2;
    private int _lastSeq;

    public PropulsionSystem(Address address, Address computer, double maxThrust, Vector3 maxTorque)
        : base(address, "Propulsion", DeviceKind.Propulsion, computer)
    {
        MaxThrust = maxThrust;
        MaxTorque = maxTorque;
        In = AddInterface("in");
        PowerDemand = BaseDemand;
    }

    public NetInterface In { get; }
    public double MaxThrust { get; }
    public Vector3 MaxTorque { get; }

    public override void Process(SimClock clock)
    {
        foreach (var iface in Interfaces)
            while (iface.Inbox.TryDequeue(out var packet)) Handle(packet, clock);
        PowerDemand = BaseDemand + DemandPerThrottle * (HasFreshSetpoint(clock) ? _throttle : 0.0);
    }

    public (Vector3 Force, Vector3 Torque) Output(SimClock clock)
    {
        if (!Powered || !HasFreshSetpoint(clock)) return (Vector3.Zero, Vector3.Zero);
        var ratio = (float)PowerSupplyRatio;
        var force = new Vector3(0, 0, (float)(_throttle * MaxThrust)) * ratio;
        var torque = _stick * MaxTorque * ratio;
        return (force, torque);
    }

    public override TelemetryReport Report()
    {
        var throttle = _throttle;
        return new TelemetryReport(Kind, PowerDemand * PowerSupplyRatio, 20 + 80 * throttle, throttle, Health);
    }

    private bool HasFreshSetpoint(SimClock clock) =>
        clock.Tick - _setpointTick <= SimConstants.SetpointHoldTicks;

    private void Handle(Packet packet, SimClock clock)
    {
        if (!packet.IsValid || packet.Dst != Address) return;
        if (packet.Kind != PacketKind.Control || packet.Topic != ControlTopic.Setpoint) return;
        if (packet.Seq <= _lastSeq) return;
        var p = packet.Payload;
        if (p.Length != 4 || !p.All(double.IsFinite)) return;

        _lastSeq = packet.Seq;
        _throttle = Math.Clamp(p[0], 0, 1);
        _stick = new Vector3(
            (float)Math.Clamp(p[1], -1, 1),    // pitch -> x
            (float)Math.Clamp(p[3], -1, 1),    // yaw   -> y
            (float)Math.Clamp(p[2], -1, 1));   // roll  -> z
        _setpointTick = clock.Tick;
    }
}
