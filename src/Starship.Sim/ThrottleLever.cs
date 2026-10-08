namespace Starship.Sim;

public sealed class ThrottleLever : Device
{
    public ThrottleLever(Address address, Address computer)
        : base(address, "Throttle", DeviceKind.Throttle, computer)
    {
        Out = AddInterface("out");
        PowerDemand = 5;
    }

    public NetInterface Out { get; }

    /// <summary>Lever position, 0 (idle) to 1 (full power).</summary>
    public double Position { get; private set; }

    public void Set(double position)
    {
        if (!double.IsFinite(position)) return;
        Position = Math.Clamp(position, 0, 1);
    }

    public override void Emit(SimClock clock)
    {
        if (IsEmitTick(clock, SimConstants.ControlPeriod))
            Broadcast(clock, Packet.Create(Address, Target, PacketKind.Control, ControlTopic.Throttle, new[] { Position }, NextSeq()));
        base.Emit(clock);
    }

    public override TelemetryReport Report() =>
        new(Kind, PowerDemand * PowerSupplyRatio, 20, Position, Health);
}
