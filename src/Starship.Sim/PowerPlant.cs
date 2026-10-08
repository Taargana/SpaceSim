namespace Starship.Sim;

public sealed class PowerPlant : Device
{
    public PowerPlant(Address address, Address computer, double capacity)
        : base(address, "PowerPlant", DeviceKind.PowerPlant, computer)
    {
        Capacity = capacity;
        Out = AddInterface("out");
    }

    public NetInterface Out { get; }
    public double Capacity { get; }
    public bool Online { get; set; } = true;
    public double Available => Online ? Capacity : 0;
    public double CurrentDemand { get; internal set; }

    private double Load => Capacity > 0 ? Math.Min(1.0, CurrentDemand / Capacity) : 0;

    public override TelemetryReport Report() =>
        new(Kind, CurrentDemand, 20 + 60 * Load, Load, Health);
}
