namespace Starship.Sim;

public static class PowerDistribution
{
    public static void Apply(PowerPlant? plant, IEnumerable<Device> devices)
    {
        var consumers = devices.Where(d => d != plant).ToList();
        var demand = consumers.Sum(d => d.PowerDemand);
        var available = plant?.Available ?? 0;
        var ratio = demand <= 0 ? (available > 0 ? 1.0 : 0.0) : Math.Min(1.0, available / demand);
        foreach (var d in consumers) d.PowerSupplyRatio = ratio;
        if (plant != null) plant.CurrentDemand = demand;
    }
}
