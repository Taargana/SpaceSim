using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class PowerTests
{
    private static TestDevice Consumer(int address, double watts)
    {
        var d = new TestDevice(address);
        d.SetDemand(watts);
        return d;
    }

    [Fact]
    public void Enough_capacity_fully_supplies_everyone()
    {
        var plant = new PowerPlant(new Address(20), new Address(1), 100);
        var a = Consumer(10, 40);
        var b = Consumer(11, 40);
        PowerDistribution.Apply(plant, new Device[] { plant, a, b });
        Assert.Equal(1.0, a.PowerSupplyRatio);
        Assert.Equal(1.0, b.PowerSupplyRatio);
        Assert.Equal(80, plant.CurrentDemand);
    }

    [Fact]
    public void Brownout_shares_power_proportionally()
    {
        var plant = new PowerPlant(new Address(20), new Address(1), 100);
        var a = Consumer(10, 100);
        var b = Consumer(11, 100);
        PowerDistribution.Apply(plant, new Device[] { plant, a, b });
        Assert.Equal(0.5, a.PowerSupplyRatio);
        Assert.Equal(0.5, b.PowerSupplyRatio);
        Assert.True(a.Powered);
    }

    [Fact]
    public void Heavy_brownout_switches_devices_off()
    {
        var plant = new PowerPlant(new Address(20), new Address(1), 100);
        var a = Consumer(10, 1000);
        PowerDistribution.Apply(plant, new Device[] { plant, a });
        Assert.Equal(0.1, a.PowerSupplyRatio, 9);
        Assert.False(a.Powered);
    }

    [Fact]
    public void Offline_plant_supplies_nothing()
    {
        var plant = new PowerPlant(new Address(20), new Address(1), 100) { Online = false };
        var a = Consumer(10, 10);
        PowerDistribution.Apply(plant, new Device[] { plant, a });
        Assert.Equal(0.0, a.PowerSupplyRatio);
        Assert.False(a.Powered);
    }

    [Fact]
    public void Missing_plant_supplies_nothing()
    {
        var a = Consumer(10, 10);
        PowerDistribution.Apply(null, new Device[] { a });
        Assert.False(a.Powered);
    }

    [Fact]
    public void The_plant_itself_stays_powered()
    {
        var plant = new PowerPlant(new Address(20), new Address(1), 100) { Online = false };
        PowerDistribution.Apply(plant, new Device[] { plant });
        Assert.True(plant.Powered);
    }

    [Fact]
    public void Plant_reports_its_load()
    {
        var plant = new PowerPlant(new Address(20), new Address(1), 200);
        PowerDistribution.Apply(plant, new Device[] { plant, Consumer(10, 50) });
        var report = plant.Report();
        Assert.Equal(DeviceKind.PowerPlant, report.Kind);
        Assert.Equal(0.25, report.Load, 9);
    }
}
