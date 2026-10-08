namespace Starship.Sim;

public sealed class Packet
{
    private Packet(Address src, Address dst, PacketKind kind, ControlTopic topic, double[] payload, int seq, uint checksum)
    {
        Src = src;
        Dst = dst;
        Kind = kind;
        Topic = topic;
        Payload = payload;
        Seq = seq;
        Checksum = checksum;
    }

    public Address Src { get; }
    public Address Dst { get; }
    public PacketKind Kind { get; }
    public ControlTopic Topic { get; }
    public double[] Payload { get; }
    public int Seq { get; }
    public uint Checksum { get; }

    public bool IsValid => Checksum == ComputeChecksum(Src, Dst, Kind, Topic, Payload, Seq);

    public static Packet Create(Address src, Address dst, PacketKind kind, ControlTopic topic, double[] payload, int seq) =>
        new(src, dst, kind, topic, payload, seq, ComputeChecksum(src, dst, kind, topic, payload, seq));

    /// <summary>Copy with damaged content and the original checksum, so <see cref="IsValid"/> is false.</summary>
    public Packet Corrupted(SimRandom rng)
    {
        var payload = (double[])Payload.Clone();
        var seq = Seq;
        if (payload.Length > 0) payload[0] += 1.0 + rng.NextDouble();
        else seq++;
        return new Packet(Src, Dst, Kind, Topic, payload, seq, Checksum);
    }

    private static uint ComputeChecksum(Address src, Address dst, PacketKind kind, ControlTopic topic, double[] payload, int seq)
    {
        uint h = 2166136261;
        h = Mix(h, (ulong)src.Value);
        h = Mix(h, (ulong)dst.Value);
        h = Mix(h, (ulong)(int)kind);
        h = Mix(h, (ulong)(int)topic);
        h = Mix(h, (ulong)seq);
        foreach (var v in payload) h = Mix(h, (ulong)BitConverter.DoubleToInt64Bits(v));
        return h;
    }

    private static uint Mix(uint h, ulong value)
    {
        for (int i = 0; i < 8; i++)
        {
            h ^= (byte)(value >> (8 * i));
            h *= 16777619;
        }
        return h;
    }
}
