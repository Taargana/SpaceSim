namespace Starship.Sim;

public sealed class RouterPort : NetInterface
{
    internal RouterPort(Router router, Device owner, int index, PortSpec spec)
        : base(owner, "port" + index)
    {
        Router = router;
        Index = index;
        Spec = spec;
    }

    public Router Router { get; }
    public int Index { get; }
    public PortSpec Spec { get; }

    /// <summary>Address of the device at the other end of the cable, null when unplugged.</summary>
    public Address? BoundAddress { get; internal set; }

    /// <summary>Last tick a valid packet from <see cref="BoundAddress"/> arrived on this port.</summary>
    public long LastRxTick { get; internal set; }
}
