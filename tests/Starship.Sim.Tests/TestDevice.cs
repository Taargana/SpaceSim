using Starship.Sim;

namespace Starship.Sim.Tests;

internal sealed class TestDevice : Device
{
    public TestDevice(int address, DeviceKind kind = DeviceKind.Throttle)
        : base(new Address(address), "test" + address, kind, new Address(1)) { }

    public void SetDemand(double watts) => PowerDemand = watts;

    public override TelemetryReport Report() => new(Kind, PowerDemand, 20, 0, Health);
}
