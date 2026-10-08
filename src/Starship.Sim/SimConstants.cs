namespace Starship.Sim;

public static class SimConstants
{
    public const int HeartbeatPeriod = 10;
    public const int TelemetryPeriod = 20;
    public const int ControlPeriod = 2;
    public const int HeartbeatTimeoutTicks = 30;
    public const int ControlTimeoutTicks = 30;
    public const int SetpointHoldTicks = 30;
    public const int TelemetryStaleTicks = 60;
    public const double PowerOffThreshold = 0.25;
}
