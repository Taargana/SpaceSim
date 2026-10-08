namespace Starship.Sim;

public sealed class Joystick : Device
{
    public Joystick(Address address, Address computer)
        : base(address, "Joystick", DeviceKind.Joystick, computer)
    {
        Out = AddInterface("out");
        PowerDemand = 5;
    }

    public NetInterface Out { get; }
    public double Pitch { get; private set; }
    public double Roll { get; private set; }
    public double Yaw { get; private set; }

    public void Set(double pitch, double roll, double yaw)
    {
        if (!double.IsFinite(pitch) || !double.IsFinite(roll) || !double.IsFinite(yaw)) return;
        Pitch = Math.Clamp(pitch, -1, 1);
        Roll = Math.Clamp(roll, -1, 1);
        Yaw = Math.Clamp(yaw, -1, 1);
    }

    public override void Emit(SimClock clock)
    {
        if (IsEmitTick(clock, SimConstants.ControlPeriod))
            Broadcast(clock, Packet.Create(Address, Target, PacketKind.Control, ControlTopic.Stick, new[] { Pitch, Roll, Yaw }, NextSeq()));
        base.Emit(clock);
    }

    public override TelemetryReport Report() =>
        new(Kind, PowerDemand * PowerSupplyRatio, 20, Math.Max(Math.Abs(Pitch), Math.Max(Math.Abs(Roll), Math.Abs(Yaw))), Health);
}
