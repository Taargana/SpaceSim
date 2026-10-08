namespace Starship.Sim;

public sealed record DeviceStatus(TelemetryReport Report, long LastTelemetryTick);

public sealed class BoardComputer : Device
{
    private readonly Dictionary<Address, DeviceStatus> _status = new();
    private readonly Dictionary<Address, int> _lastSeq = new();
    private long _throttleTick = long.MinValue / 2;
    private long _stickTick = long.MinValue / 2;
    private double _stickPitch, _stickRoll, _stickYaw;

    public BoardComputer(Address address, RouterModel model)
        : base(address, "BoardComputer", DeviceKind.BoardComputer, address)
    {
        Router = new Router(this, model);
        PowerDemand = 30;
    }

    public Router Router { get; }
    public double CommandedThrottle { get; private set; }
    public (double Pitch, double Roll, double Yaw) CommandedStick => (_stickPitch, _stickRoll, _stickYaw);
    public IReadOnlyDictionary<Address, DeviceStatus> Status => _status;

    public long AgeOf(Address address, SimClock clock) =>
        _status.TryGetValue(address, out var s) ? clock.Tick - s.LastTelemetryTick : long.MaxValue;

    public bool IsStale(Address address, SimClock clock) =>
        AgeOf(address, clock) > SimConstants.TelemetryStaleTicks;

    /// <summary>The computer talks through its router, never through its own interfaces.</summary>
    public override void Emit(SimClock clock) { }

    public override void Process(SimClock clock)
    {
        while (Router.LocalInbox.TryDequeue(out var packet)) Handle(packet, clock);
        SendSetpoint(clock);
    }

    public override TelemetryReport Report() =>
        new(Kind, PowerDemand * PowerSupplyRatio, 20, 0, Health);

    private void Handle(Packet packet, SimClock clock)
    {
        if (_lastSeq.TryGetValue(packet.Src, out var last) && packet.Seq <= last) return;
        _lastSeq[packet.Src] = packet.Seq;

        switch (packet.Kind)
        {
            case PacketKind.Control when packet.Topic == ControlTopic.Throttle && IsFinite(packet.Payload, 1):
                CommandedThrottle = Math.Clamp(packet.Payload[0], 0, 1);
                _throttleTick = clock.Tick;
                break;
            case PacketKind.Control when packet.Topic == ControlTopic.Stick && IsFinite(packet.Payload, 3):
                _stickPitch = Math.Clamp(packet.Payload[0], -1, 1);
                _stickRoll = Math.Clamp(packet.Payload[1], -1, 1);
                _stickYaw = Math.Clamp(packet.Payload[2], -1, 1);
                _stickTick = clock.Tick;
                break;
            case PacketKind.Telemetry when TelemetryReport.TryFromPayload(packet.Payload, out var report):
                _status[packet.Src] = new DeviceStatus(report, clock.Tick);
                break;
        }
    }

    private void SendSetpoint(SimClock clock)
    {
        if (!IsEmitTick(clock, SimConstants.ControlPeriod)) return;
        if (clock.Tick - _throttleTick > SimConstants.ControlTimeoutTicks) return;
        // The freshest non-stale propulsion wins (lowest address on ties), so a re-addressed thruster is followed.
        var candidates = _status
            .Where(kv => kv.Value.Report.Kind == DeviceKind.Propulsion && !IsStale(kv.Key, clock))
            .ToList();
        if (candidates.Count == 0) return;
        var propulsion = candidates
            .OrderByDescending(kv => kv.Value.LastTelemetryTick)
            .ThenBy(kv => kv.Key.Value)
            .First();

        var stickFresh = clock.Tick - _stickTick <= SimConstants.ControlTimeoutTicks;
        var payload = new[]
        {
            CommandedThrottle,
            stickFresh ? _stickPitch : 0.0,
            stickFresh ? _stickRoll : 0.0,
            stickFresh ? _stickYaw : 0.0,
        };
        Router.Submit(Packet.Create(Address, propulsion.Key, PacketKind.Control, ControlTopic.Setpoint, payload, NextSeq()));
    }

    private static bool IsFinite(double[] payload, int length) =>
        payload.Length == length && payload.All(double.IsFinite);
}
