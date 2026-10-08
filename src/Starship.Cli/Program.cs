using System.Diagnostics;
using System.Numerics;
using System.Text;
using Starship.Sim;

var routerName = ArgValue("--router") ?? "basic";
var model = routerName switch
{
    "basic" => RouterModels.Basic,
    "intermediate" => RouterModels.Intermediate,
    "advanced" => RouterModels.Advanced,
    _ => throw new ArgumentException($"Unknown router '{routerName}' (basic, intermediate, advanced)"),
};
var ship = ShipBuilder.CreateDefault(seed: 1, model, cableQuality: 0.9);

var cables = ship.Cables.ToDictionary(kv => kv.Key, kv => kv.Value);

var headless = ArgValue("--headless");
if (headless != null)
{
    ship.Throttle.Set(0.5);
    for (int i = 0; i < int.Parse(headless); i++) ship.Ship.Step();
    Console.WriteLine(Render(ship));
    return 0;
}

Console.CursorVisible = false;
Console.Clear();
double throttle = 0, pitch = 0, roll = 0, yaw = 0;
var timer = Stopwatch.StartNew();
double last = 0, accumulator = 0;

while (true)
{
    while (Console.KeyAvailable)
    {
        switch (Console.ReadKey(true).Key)
        {
            case ConsoleKey.W: throttle = Math.Min(1, throttle + 0.05); break;
            case ConsoleKey.S: throttle = Math.Max(0, throttle - 0.05); break;
            case ConsoleKey.UpArrow: pitch = Math.Min(1, pitch + 0.25); break;
            case ConsoleKey.DownArrow: pitch = Math.Max(-1, pitch - 0.25); break;
            case ConsoleKey.RightArrow: yaw = Math.Min(1, yaw + 0.25); break;
            case ConsoleKey.LeftArrow: yaw = Math.Max(-1, yaw - 0.25); break;
            case ConsoleKey.D: roll = Math.Min(1, roll + 0.25); break;
            case ConsoleKey.A: roll = Math.Max(-1, roll - 0.25); break;
            case ConsoleKey.Spacebar: pitch = roll = yaw = 0; break;
            case ConsoleKey.X: cables["throttle"].Burn(); break;
            case ConsoleKey.R: ReplaceBurntCables(); break;
            case ConsoleKey.Q: Console.CursorVisible = true; Console.Clear(); return 0;
        }
        ship.Throttle.Set(throttle);
        ship.Joystick.Set(pitch, roll, yaw);
    }

    var now = timer.Elapsed.TotalSeconds;
    accumulator += now - last;
    last = now;
    while (accumulator >= SimClock.Dt)
    {
        ship.Ship.Step();
        accumulator -= SimClock.Dt;
    }

    Console.SetCursorPosition(0, 0);
    Console.Write(Render(ship));
    Thread.Sleep(16);
}

string? ArgValue(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

void ReplaceBurntCables()
{
    foreach (var (key, cable) in cables.Where(kv => kv.Value.IsBurnt).ToList())
    {
        var fresh = new Cable(cable.Name + "'", 0.9, cable.Length);
        ship.Ship.Replace(cable, fresh);
        cables[key] = fresh;
    }
}

static string Render(DefaultShip s)
{
    var body = s.Ship.Body;
    var forward = Vector3.Transform(Vector3.UnitZ, body.Orientation);
    var clock = s.Ship.Clock;
    var sb = new StringBuilder();
    void Line(string text) => sb.AppendLine(text.PadRight(100));

    Line($"Router: {s.Computer.Router.Model.Name}   tick {clock.Tick}   dropped packets: {s.Computer.Router.DroppedCount}");
    Line($"Throttle {s.Throttle.Position,4:P0}   stick pitch {s.Joystick.Pitch,5:F2} roll {s.Joystick.Roll,5:F2} yaw {s.Joystick.Yaw,5:F2}");
    Line($"Position  {body.Position.X,9:F1} {body.Position.Y,9:F1} {body.Position.Z,9:F1}");
    Line($"Velocity  {body.Velocity.X,9:F1} {body.Velocity.Y,9:F1} {body.Velocity.Z,9:F1}  speed {body.Velocity.Length(),8:F1} m/s");
    Line($"Heading   {forward.X,9:F2} {forward.Y,9:F2} {forward.Z,9:F2}   spin {body.AngularVelocity.Length(),6:F2} rad/s");
    Line("");
    Line("Telemetry (as known by the board computer)");
    foreach (var (address, status) in s.Computer.Status.OrderBy(kv => kv.Key.Value))
    {
        var stale = s.Computer.IsStale(address, clock);
        Line($"  {status.Report.Kind,-12} {address,-5} age {s.Computer.AgeOf(address, clock),4} ticks  " +
             $"load {status.Report.Load,4:P0}  temp {status.Report.Temperature,5:F0}C  {(stale ? "STALE" : "ok")}");
    }
    Line("");
    Line("Cables");
    foreach (var cable in s.Ship.Cables)
        Line($"  {cable.Name,-12} {cable.Length,4:F0} m  quality {cable.Quality:F2}  health {cable.Health,4:P0}  {(cable.IsBurnt ? "BURNT" : "ok")}");
    Line("");
    Line("W/S throttle  arrows pitch/yaw  A/D roll  space neutral  X burn throttle cable  R replace burnt  Q quit");
    return sb.ToString();
}
