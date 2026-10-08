namespace Starship.Sim;

public readonly record struct TelemetryReport(DeviceKind Kind, double Power, double Temperature, double Load, double Health)
{
    public double[] ToPayload() => new[] { (double)(int)Kind, Power, Temperature, Load, Health };

    public static bool TryFromPayload(double[] payload, out TelemetryReport report)
    {
        report = default;
        if (payload.Length != 5 || !payload.All(double.IsFinite)) return false;
        var kind = (int)payload[0];
        if (!Enum.IsDefined(typeof(DeviceKind), kind)) return false;
        report = new TelemetryReport((DeviceKind)kind, payload[1], payload[2], payload[3], payload[4]);
        return true;
    }
}
