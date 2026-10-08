using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class PacketTests
{
    private static Packet Make(double[]? payload = null) =>
        Packet.Create(new Address(10), new Address(1), PacketKind.Control, ControlTopic.Throttle, payload ?? new[] { 0.5 }, 7);

    [Fact]
    public void Fresh_packet_is_valid()
    {
        Assert.True(Make().IsValid);
    }

    [Fact]
    public void Corrupted_packet_is_invalid_but_keeps_addressing()
    {
        var rng = new SimRandom(1);
        var bad = Make().Corrupted(rng);
        Assert.False(bad.IsValid);
        Assert.Equal(10, bad.Src.Value);
        Assert.Equal(1, bad.Dst.Value);
    }

    [Fact]
    public void Corrupting_empty_payload_still_invalidates()
    {
        var bad = Make(Array.Empty<double>()).Corrupted(new SimRandom(1));
        Assert.False(bad.IsValid);
    }

    [Fact]
    public void Corruption_does_not_alter_the_original()
    {
        var p = Make();
        p.Corrupted(new SimRandom(1));
        Assert.True(p.IsValid);
        Assert.Equal(0.5, p.Payload[0]);
    }

    [Fact]
    public void Tampered_payload_is_detected()
    {
        var p = Make();
        p.Payload[0] = 0.6;
        Assert.False(p.IsValid);
    }

    [Fact]
    public void Telemetry_report_round_trips()
    {
        var r = new TelemetryReport(DeviceKind.Propulsion, 120, 55, 0.4, 0.9);
        Assert.True(TelemetryReport.TryFromPayload(r.ToPayload(), out var back));
        Assert.Equal(r, back);
    }

    [Theory]
    [InlineData(new double[] { 4, 1, 1, 1 })]
    [InlineData(new double[] { 99, 1, 1, 1, 1 })]
    [InlineData(new double[] { 4, double.NaN, 1, 1, 1 })]
    [InlineData(new double[] { 4, 1, 1, 1, double.PositiveInfinity })]
    public void Telemetry_report_rejects_malformed_payload(double[] payload)
    {
        Assert.False(TelemetryReport.TryFromPayload(payload, out _));
    }
}
