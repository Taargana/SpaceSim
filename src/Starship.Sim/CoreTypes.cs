namespace Starship.Sim;

public readonly record struct Address(int Value)
{
    public override string ToString() => "#" + Value;
}

public enum DeviceKind { Throttle, Joystick, BoardComputer, PowerPlant, Propulsion }

public enum PacketKind { Control, Telemetry, Heartbeat }

public enum ControlTopic { None, Throttle, Stick, Setpoint }
