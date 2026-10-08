namespace Starship.Sim;

public class NetInterface
{
    protected internal NetInterface(Device owner, string name)
    {
        Owner = owner;
        Name = name;
    }

    public Device Owner { get; }
    public string Name { get; }
    public Cable? Cable { get; internal set; }
    public bool IsConnected => Cable != null;
    public Queue<Packet> Inbox { get; } = new();

    public void Send(Packet packet, SimClock clock) => Cable?.Transmit(this, packet, clock);
}
