namespace Starship.Sim;

/// <summary>A port is either dedicated to one device kind or generic (null).</summary>
public sealed record PortSpec(DeviceKind? Dedicated);

public sealed record RouterCapabilities(bool ConfigurableAddressing, bool RoutingTable, bool Failover, int MaxRoutesPerDst);

public sealed record RouterModel(
    string Name,
    IReadOnlyList<PortSpec> Ports,
    RouterCapabilities Capabilities,
    int Throughput,
    int QueueCapacity);

public static class RouterModels
{
    private static PortSpec[] DedicatedPorts() => new[]
    {
        new PortSpec(DeviceKind.Throttle),
        new PortSpec(DeviceKind.Joystick),
        new PortSpec(DeviceKind.Propulsion),
        new PortSpec(DeviceKind.PowerPlant),
    };

    public static readonly RouterModel Basic = new(
        "Basic", DedicatedPorts(), new RouterCapabilities(false, false, false, 1), 8, 32);

    public static readonly RouterModel Intermediate = new(
        "Intermediate",
        DedicatedPorts().Concat(Enumerable.Repeat(new PortSpec(null), 4)).ToArray(),
        new RouterCapabilities(false, true, true, 2), 16, 64);

    public static readonly RouterModel Advanced = new(
        "Advanced",
        Enumerable.Repeat(new PortSpec(null), 16).ToArray(),
        new RouterCapabilities(true, true, true, 4), 64, 128);
}
