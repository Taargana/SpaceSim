# Simulation de vaisseau (jalon 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construire le cœur de simulation d'un vaisseau (cockpit → câbles → ordinateur de bord + routeur → propulsion, énergie, télémétrie, physique newtonienne 3D) en C#, avec une interface console.

**Architecture:** Bibliothèque `Starship.Sim` sans rendu. Réseau à paquets adressés: appareils, `NetInterface`, `Cable` (bruit, latence, santé), `Router` piloté par un `RouterModel` (gammes). `Ship.Step()` exécute un tick fixe 60 Hz déterministe (seed). L'énergie (répartition proportionnelle) et la physique (`ShipBody`) sont continues, tout le reste passe en paquets.

**Tech Stack:** C# / .NET 8 (`net8.0`), xUnit, `System.Numerics`. SDK installé: 10.0.103 (8.0 aussi présent).

**Spec:** `docs/superpowers/specs/2026-10-08-ship-simulation-design.md`

## Global Constraints

- C# (.NET), bibliothèque de simulation pure: aucune dépendance de rendu ni d'E/S dans `Starship.Sim`.
- Tick fixe 60 Hz (`SimClock.Dt = 1/60`), RNG avec seed, simulation déterministe à seed fixe.
- Physique newtonienne 3D, sans amortissement magique (la vitesse persiste sans poussée).
- Communication par paquets adressés `Packet { Src, Dst, Kind, Payload, Seq, Checksum }`, `Kind` ∈ `Control`, `Telemetry`, `Heartbeat`.
- Topologie en graphe modifiable à l'exécution: `Connect`, `Disconnect`, `Replace`.
- Le mode de routage est une capacité du `RouterModel` (gamme Basique / Intermédiaire / Avancé), un seul moteur de routage.
- Découverte dynamique de topologie, énergie par câble de puissance, composants dupliqués avec vote, rendu 3D: hors périmètre.
- Précisions de conception apportées par ce plan (reportées dans la spec en Task 1): champ `Topic` sur `Packet`, liaison de port (`BoundAddress`), sens de `ConfigurableAddressing` et `RoutingTable`.
- Types flottants: `double` pour la simulation réseau/énergie, `float` (`System.Numerics`) pour la physique. À revisiter si la précision à grande distance devient un problème.

## Review Focus

Entrées ou conditions que la spec laisse implicites, les plus probables d'abord. Chaque ligne a son test dans la tâche indiquée.

1. Payload d'un appareil défectueux avec checksum valide: `NaN`, `∞`, mauvaise longueur, throttle hors [0,1]. Attendu: ignoré ou borné, jamais propagé à la physique (Task 9, Task 10).
2. Paquet adressé à une destination inconnue du routeur. Attendu: abandonné et compté, sans crash (Task 4).
3. Retour de l'énergie après une panne. Attendu: aucune commande périmée rejouée, reprise normale (Task 11).
4. Appareil sans aucun câble qui émet. Attendu: aucun effet, aucune exception (Task 7).
5. Câble remplacé ou endommagé avec des paquets en vol. Attendu: ces paquets sont perdus, les nouveaux passent (Task 3, Task 6).

---

## File Structure

```
Starship.slnx (ou .sln)
src/Starship.Sim/
  SimRandom.cs         RNG xorshift déterministe
  SimClock.cs          tick, Dt, RNG
  SimConstants.cs      périodes, timeouts, seuils
  CoreTypes.cs         Address, DeviceKind, PacketKind, ControlTopic
  Packet.cs            paquet + checksum + corruption
  TelemetryReport.cs   rapport d'état + (dé)sérialisation payload
  NetInterface.cs      prise réseau d'un appareil
  Cable.cs             liaison bruitée, latence, santé
  Device.cs            base des appareils (émission, énergie)
  RouterModel.cs       PortSpec, RouterCapabilities, RouterModel, RouterModels
  RouterPort.cs        port de routeur (liaison d'adresse)
  Router.cs            moteur de routage
  ShipBody.cs          physique newtonienne
  Ship.cs              graphe d'appareils/câbles, Step()
  ThrottleLever.cs     manette de puissance
  Joystick.cs          joystick
  PowerPlant.cs        groupe électrique
  PowerDistribution.cs répartition proportionnelle
  BoardComputer.cs     ordinateur de bord (+ Router)
  PropulsionSystem.cs  propulsion
  ShipBuilder.cs       vaisseau par défaut
src/Starship.Cli/Program.cs
tests/Starship.Sim.Tests/   un fichier de tests par unité + TestDevice.cs + TestWiring.cs
```

---

### Task 1: Scaffold, SimRandom, SimClock, alignement de la spec

**Files:**
- Create: `Starship.slnx`, `.gitignore`, `src/Starship.Sim/Starship.Sim.csproj`, `src/Starship.Sim/SimRandom.cs`, `src/Starship.Sim/SimClock.cs`, `src/Starship.Sim/SimConstants.cs`
- Create: `src/Starship.Cli/` (template console), `tests/Starship.Sim.Tests/` (template xunit)
- Test: `tests/Starship.Sim.Tests/SimRandomTests.cs`, `tests/Starship.Sim.Tests/SimClockTests.cs`
- Modify: `docs/superpowers/specs/2026-10-08-ship-simulation-design.md`

**Interfaces:**
- Produces: `SimRandom(int seed)`, `ulong NextUInt64()`, `double NextDouble()` dans [0,1). `SimClock(int seed)`: `const int TicksPerSecond = 60`, `const double Dt`, `long Tick`, `SimRandom Rng`, `void Advance()`. `SimConstants`: `HeartbeatPeriod=10`, `TelemetryPeriod=20`, `ControlPeriod=2`, `HeartbeatTimeoutTicks=30`, `ControlTimeoutTicks=30`, `SetpointHoldTicks=30`, `TelemetryStaleTicks=60`, `PowerOffThreshold=0.25`.

- [ ] **Step 1: Initialiser le dépôt et les projets**

```bash
cd /Users/yleblevec/Github/testTrain
git init
dotnet new gitignore
dotnet new sln -n Starship
dotnet new classlib -o src/Starship.Sim -f net8.0
dotnet new console -o src/Starship.Cli -f net8.0
dotnet new xunit -o tests/Starship.Sim.Tests -f net8.0
rm src/Starship.Sim/Class1.cs tests/Starship.Sim.Tests/UnitTest1.cs
dotnet sln add src/Starship.Sim src/Starship.Cli tests/Starship.Sim.Tests
dotnet add src/Starship.Cli reference src/Starship.Sim
dotnet add tests/Starship.Sim.Tests reference src/Starship.Sim
```

Remplacer le contenu de `src/Starship.Sim/Starship.Sim.csproj` par:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Starship.Sim.Tests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Écrire les tests (échouent: types absents)**

`tests/Starship.Sim.Tests/SimRandomTests.cs`:

```csharp
using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class SimRandomTests
{
    [Fact]
    public void Same_seed_gives_same_sequence()
    {
        var a = new SimRandom(7);
        var b = new SimRandom(7);
        for (int i = 0; i < 100; i++) Assert.Equal(a.NextDouble(), b.NextDouble());
    }

    [Fact]
    public void Different_seeds_give_different_sequences()
    {
        var a = new SimRandom(1);
        var b = new SimRandom(2);
        bool anyDifferent = false;
        for (int i = 0; i < 10; i++) anyDifferent |= a.NextDouble() != b.NextDouble();
        Assert.True(anyDifferent);
    }

    [Fact]
    public void NextDouble_stays_in_unit_range()
    {
        var r = new SimRandom(3);
        for (int i = 0; i < 10_000; i++)
        {
            var v = r.NextDouble();
            Assert.InRange(v, 0.0, 0.9999999999999999);
        }
    }
}
```

`tests/Starship.Sim.Tests/SimClockTests.cs`:

```csharp
using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class SimClockTests
{
    [Fact]
    public void Starts_at_tick_zero_and_advances()
    {
        var clock = new SimClock(1);
        Assert.Equal(0, clock.Tick);
        clock.Advance();
        clock.Advance();
        Assert.Equal(2, clock.Tick);
    }

    [Fact]
    public void Dt_is_one_sixtieth()
    {
        Assert.Equal(1.0 / 60.0, SimClock.Dt, 12);
    }
}
```

- [ ] **Step 3: Lancer, vérifier l'échec**

Run: `dotnet test`
Expected: FAIL (erreur de compilation: `SimRandom`, `SimClock` introuvables).

- [ ] **Step 4: Implémenter**

`src/Starship.Sim/SimRandom.cs`:

```csharp
namespace Starship.Sim;

/// <summary>Deterministic xorshift64* generator. Own implementation so results never depend on the .NET version.</summary>
public sealed class SimRandom
{
    private ulong _state;

    public SimRandom(int seed)
    {
        _state = (ulong)seed * 0x9E3779B97F4A7C15UL + 0xD1B54A32D192ED03UL;
        if (_state == 0) _state = 1;
    }

    public ulong NextUInt64()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return _state * 0x2545F4914F6CDD1DUL;
    }

    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);
}
```

`src/Starship.Sim/SimClock.cs`:

```csharp
namespace Starship.Sim;

public sealed class SimClock
{
    public const int TicksPerSecond = 60;
    public const double Dt = 1.0 / TicksPerSecond;

    public SimClock(int seed) => Rng = new SimRandom(seed);

    public long Tick { get; private set; }
    public SimRandom Rng { get; }

    public void Advance() => Tick++;
}
```

`src/Starship.Sim/SimConstants.cs`:

```csharp
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
```

- [ ] **Step 5: Lancer, vérifier le succès**

Run: `dotnet test`
Expected: PASS (5 tests).

- [ ] **Step 6: Aligner la spec**

Dans `docs/superpowers/specs/2026-10-08-ship-simulation-design.md`, section 4.1, remplacer les deux lignes
`- \`Packet { Src, Dst, Kind, Payload, Seq, Checksum }\`.` et `- \`Kind\`: \`Control\`, \`Telemetry\`, \`Heartbeat\`.` par:

```
- `Packet { Src, Dst, Kind, Topic, Payload, Seq, Checksum }`.
- `Kind`: `Control`, `Telemetry`, `Heartbeat`. `Topic` précise un paquet `Control`: `Throttle`, `Stick`, `Setpoint`.
```

Dans la section 4.4, ajouter à la fin:

```
Liaison de port: chaque port connecté est lié (`BoundAddress`) à l'adresse de l'appareil à l'autre bout du câble. Le routeur envoie vers `Dst` par les ports liés à `Dst` (plusieurs ports liés = routes redondantes, classées par index). `RoutingTable` permet de fixer des routes explicites (`SetRoute`) prioritaires sur ces routes automatiques. `ConfigurableAddressing` permet d'assigner l'adresse d'un appareil via son port (`AssignAddress`). Un port est considéré `Up` s'il a reçu un paquet valide de l'adresse liée depuis moins de 30 ticks.
```

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "chore: scaffold solution, SimRandom, SimClock, align spec"
```

---

### Task 2: Types de base, Packet, TelemetryReport

**Files:**
- Create: `src/Starship.Sim/CoreTypes.cs`, `src/Starship.Sim/Packet.cs`, `src/Starship.Sim/TelemetryReport.cs`
- Test: `tests/Starship.Sim.Tests/PacketTests.cs`

**Interfaces:**
- Consumes: `SimRandom` (Task 1).
- Produces: `readonly record struct Address(int Value)`; `enum DeviceKind { Throttle, Joystick, BoardComputer, PowerPlant, Propulsion }`; `enum PacketKind { Control, Telemetry, Heartbeat }`; `enum ControlTopic { None, Throttle, Stick, Setpoint }`. `Packet`: propriétés `Address Src, Dst; PacketKind Kind; ControlTopic Topic; double[] Payload; int Seq; uint Checksum; bool IsValid`; `static Packet Create(Address src, Address dst, PacketKind kind, ControlTopic topic, double[] payload, int seq)`; `Packet Corrupted(SimRandom rng)`. `readonly record struct TelemetryReport(DeviceKind Kind, double Power, double Temperature, double Load, double Health)` avec `double[] ToPayload()` et `static bool TryFromPayload(double[] payload, out TelemetryReport report)` (payload de 5 valeurs, toutes finies, `Kind` défini).

- [ ] **Step 1: Écrire les tests**

`tests/Starship.Sim.Tests/PacketTests.cs`:

```csharp
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
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `dotnet test --filter "FullyQualifiedName~PacketTests"`
Expected: FAIL (compilation: `Address`, `Packet` introuvables).

- [ ] **Step 3: Implémenter**

`src/Starship.Sim/CoreTypes.cs`:

```csharp
namespace Starship.Sim;

public readonly record struct Address(int Value)
{
    public override string ToString() => "#" + Value;
}

public enum DeviceKind { Throttle, Joystick, BoardComputer, PowerPlant, Propulsion }

public enum PacketKind { Control, Telemetry, Heartbeat }

public enum ControlTopic { None, Throttle, Stick, Setpoint }
```

`src/Starship.Sim/Packet.cs`:

```csharp
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
```

`src/Starship.Sim/TelemetryReport.cs`:

```csharp
namespace Starship.Sim;

public readonly record struct TelemetryReport(DeviceKind Kind, double Power, double Temperature, double Load, double Health)
{
    public double[] ToPayload() => new[] { (double)(int)Kind, Power, Temperature, Load, Health };

    public static bool TryFromPayload(double[] payload, out TelemetryReport report)
    {
        report = default;
        if (payload.Length != 5 || !payload.All(double.IsFinite)) return false;
        var kind = (int)payload[0];
        if (!Enum.IsDefined(typeof(DeviceKind), kind)) return false;
        report = new TelemetryReport((DeviceKind)kind, payload[1], payload[2], payload[3], payload[4]);
        return true;
    }
}
```

- [ ] **Step 4: Lancer, vérifier le succès**

Run: `dotnet test --filter "FullyQualifiedName~PacketTests"`
Expected: PASS (10 cas).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: core types, Packet with checksum, TelemetryReport"
```

---

### Task 3: Device, NetInterface, Cable

**Files:**
- Create: `src/Starship.Sim/NetInterface.cs`, `src/Starship.Sim/Cable.cs`, `src/Starship.Sim/Device.cs`
- Create (tests): `tests/Starship.Sim.Tests/TestDevice.cs`, `tests/Starship.Sim.Tests/TestWiring.cs`
- Test: `tests/Starship.Sim.Tests/CableTests.cs`, `tests/Starship.Sim.Tests/DeviceTests.cs`

**Interfaces:**
- Consumes: `Packet`, `Address`, `DeviceKind`, `TelemetryReport`, `SimClock`, `SimConstants`.
- Produces:
  - `NetInterface`: `Device Owner`, `string Name`, `Cable? Cable { get; internal set; }`, `bool IsConnected`, `Queue<Packet> Inbox`, `void Send(Packet, SimClock)`; constructeur `protected internal NetInterface(Device owner, string name)`.
  - `Cable(string name, double quality, double length)`: `Name, Quality, Length, Health, IsBurnt, A, B, ErrorRate, LatencyTicks`, `Damage(double)`, `Burn()`, `NetInterface Peer(NetInterface)`, internes `Attach(a, b)`, `Detach()`, `Transmit(from, packet, clock)`, `Step(clock)`.
  - `Device` (abstract): ctor `protected Device(Address address, string name, DeviceKind kind, Address target)`; `Address Address { get; internal set; }`, `Name`, `Kind`, `Target`, `IReadOnlyList<NetInterface> Interfaces`, `NetInterface AddInterface(string name)`, `double Health {get;set;}`, `double PowerDemand {get; protected set;}`, `double PowerSupplyRatio {get; internal set;}`, `bool Powered`, `void DiscardInbound()`, `virtual void Emit(SimClock)`, `virtual void Process(SimClock)`, `abstract TelemetryReport Report()`; protégés: `int NextSeq()`, `void Broadcast(SimClock, Packet)`, `bool IsEmitTick(SimClock, int basePeriod)`.

Formules de câble (reprises telles quelles dans le code): `ErrorRate = clamp((1-Quality)*0.2 + Length*0.002 + (1-Health)*0.3, 0, 1)` (1 si brûlé); perte avec probabilité `ErrorRate*0.5`; sinon corruption avec probabilité `ErrorRate*0.5`; `LatencyTicks = 1 + (int)(Length/50)`.

- [ ] **Step 1: Écrire les aides de test**

`tests/Starship.Sim.Tests/TestDevice.cs`:

```csharp
using Starship.Sim;

namespace Starship.Sim.Tests;

internal sealed class TestDevice : Device
{
    public TestDevice(int address, DeviceKind kind = DeviceKind.Throttle)
        : base(new Address(address), "test" + address, kind, new Address(1)) { }

    public void SetDemand(double watts) => PowerDemand = watts;

    public override TelemetryReport Report() => new(Kind, PowerDemand, 20, 0, Health);
}
```

`tests/Starship.Sim.Tests/TestWiring.cs`:

```csharp
using Starship.Sim;

namespace Starship.Sim.Tests;

internal static class TestWiring
{
    public static Cable Link(NetInterface a, NetInterface b, double quality = 1.0, double length = 0.0)
    {
        var cable = new Cable("test", quality, length);
        cable.Attach(a, b);
        return cable;
    }

    /// <summary>Advance one tick, then let the cables deliver what has arrived.</summary>
    public static void Pump(SimClock clock, params Cable[] cables)
    {
        clock.Advance();
        foreach (var c in cables) c.Step(clock);
    }
}
```

- [ ] **Step 2: Écrire les tests**

`tests/Starship.Sim.Tests/CableTests.cs`:

```csharp
using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class CableTests
{
    private static Packet P(int seq = 1) =>
        Packet.Create(new Address(10), new Address(11), PacketKind.Heartbeat, ControlTopic.None, Array.Empty<double>(), seq);

    private static (SimClock clock, NetInterface a, NetInterface b) Pair()
    {
        var clock = new SimClock(1);
        var a = new TestDevice(10).AddInterface("x");
        var b = new TestDevice(11).AddInterface("x");
        return (clock, a, b);
    }

    [Fact]
    public void Perfect_short_cable_delivers_after_latency()
    {
        var (clock, a, b) = Pair();
        var cable = TestWiring.Link(a, b);
        a.Send(P(), clock);
        cable.Step(clock);
        Assert.Empty(b.Inbox);
        TestWiring.Pump(clock, cable);
        var got = Assert.Single(b.Inbox);
        Assert.True(got.IsValid);
    }

    [Fact]
    public void Latency_grows_with_length()
    {
        Assert.Equal(1, new Cable("c", 1, 0).LatencyTicks);
        Assert.Equal(3, new Cable("c", 1, 100).LatencyTicks);
    }

    [Fact]
    public void Burnt_cable_transmits_nothing()
    {
        var (clock, a, b) = Pair();
        var cable = TestWiring.Link(a, b);
        cable.Burn();
        Assert.True(cable.IsBurnt);
        a.Send(P(), clock);
        TestWiring.Pump(clock, cable);
        Assert.Empty(b.Inbox);
    }

    [Fact]
    public void Burning_drops_packets_already_in_flight()
    {
        var (clock, a, b) = Pair();
        var cable = TestWiring.Link(a, b);
        a.Send(P(), clock);
        cable.Burn();
        TestWiring.Pump(clock, cable);
        Assert.Empty(b.Inbox);
    }

    [Fact]
    public void Detaching_drops_packets_already_in_flight_and_frees_interfaces()
    {
        var (clock, a, b) = Pair();
        var cable = TestWiring.Link(a, b);
        a.Send(P(), clock);
        cable.Detach();
        TestWiring.Pump(clock, cable);
        Assert.Empty(b.Inbox);
        Assert.Null(a.Cable);
        Assert.Null(b.Cable);
    }

    [Fact]
    public void Error_rate_is_clamped_to_one()
    {
        var c = new Cable("c", 0, 1000);
        c.Damage(0.5);
        Assert.Equal(1.0, c.ErrorRate);
    }

    [Fact]
    public void Peer_returns_the_other_end()
    {
        var (_, a, b) = Pair();
        var cable = TestWiring.Link(a, b);
        Assert.Same(b, cable.Peer(a));
        Assert.Same(a, cable.Peer(b));
    }

    private static int ValidDelivered(double quality, double length, double damage = 0)
    {
        var clock = new SimClock(42);
        var a = new TestDevice(10).AddInterface("x");
        var b = new TestDevice(11).AddInterface("x");
        var cable = TestWiring.Link(a, b, quality, length);
        cable.Damage(damage);
        for (int i = 0; i < 2000; i++) a.Send(P(i), clock);
        for (int i = 0; i < 10; i++) TestWiring.Pump(clock, cable);
        return b.Inbox.Count(p => p.IsValid);
    }

    [Fact]
    public void Zero_length_perfect_cable_loses_nothing()
    {
        Assert.Equal(2000, ValidDelivered(1.0, 0.0));
    }

    [Fact]
    public void Longer_cable_delivers_fewer_valid_packets()
    {
        Assert.True(ValidDelivered(0.5, 200) < ValidDelivered(0.5, 0));
    }

    [Fact]
    public void Lower_quality_delivers_fewer_valid_packets()
    {
        Assert.True(ValidDelivered(0.2, 10) < ValidDelivered(0.9, 10));
    }

    [Fact]
    public void Damaged_cable_delivers_fewer_valid_packets()
    {
        Assert.True(ValidDelivered(1.0, 10, damage: 0.6) < ValidDelivered(1.0, 10));
    }
}
```

`tests/Starship.Sim.Tests/DeviceTests.cs`:

```csharp
using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class DeviceTests
{
    [Fact]
    public void Emits_heartbeat_and_telemetry_on_tick_zero()
    {
        var clock = new SimClock(1);
        var dev = new TestDevice(10);
        var iface = dev.AddInterface("x");
        var peer = new TestDevice(11).AddInterface("x");
        var cable = TestWiring.Link(iface, peer);
        dev.Emit(clock);
        TestWiring.Pump(clock, cable);
        Assert.Contains(peer.Inbox, p => p.Kind == PacketKind.Heartbeat && p.Src.Value == 10 && p.Dst.Value == 1);
        Assert.Contains(peer.Inbox, p => p.Kind == PacketKind.Telemetry);
    }

    [Fact]
    public void Same_message_goes_out_on_every_interface_with_the_same_seq()
    {
        var clock = new SimClock(1);
        var dev = new TestDevice(10);
        var i1 = dev.AddInterface("a");
        var i2 = dev.AddInterface("b");
        var p1 = new TestDevice(11).AddInterface("x");
        var p2 = new TestDevice(12).AddInterface("x");
        var c1 = TestWiring.Link(i1, p1);
        var c2 = TestWiring.Link(i2, p2);
        dev.Emit(clock);
        TestWiring.Pump(clock, c1, c2);
        var hb1 = p1.Inbox.Single(p => p.Kind == PacketKind.Heartbeat);
        var hb2 = p2.Inbox.Single(p => p.Kind == PacketKind.Heartbeat);
        Assert.Equal(hb1.Seq, hb2.Seq);
    }

    [Fact]
    public void Device_without_any_cable_emits_without_error()
    {
        var clock = new SimClock(1);
        var dev = new TestDevice(10);
        dev.AddInterface("unplugged");
        var ex = Record.Exception(() => dev.Emit(clock));
        Assert.Null(ex);
    }

    [Fact]
    public void Low_power_slows_emission()
    {
        int Count(double ratio)
        {
            var clock = new SimClock(1);
            var dev = new TestDevice(10) { };
            dev.PowerSupplyRatio = ratio;
            var iface = dev.AddInterface("x");
            var peer = new TestDevice(11).AddInterface("x");
            var cable = TestWiring.Link(iface, peer);
            for (int i = 0; i < 40; i++)
            {
                dev.Emit(clock);
                TestWiring.Pump(clock, cable);
            }
            return peer.Inbox.Count(p => p.Kind == PacketKind.Heartbeat);
        }

        Assert.Equal(4, Count(1.0));   // ticks 0, 10, 20, 30
        Assert.Equal(2, Count(0.5));   // period 20: ticks 0, 20
    }

    [Fact]
    public void Powered_flag_follows_threshold()
    {
        var dev = new TestDevice(10);
        dev.PowerSupplyRatio = 0.3;
        Assert.True(dev.Powered);
        dev.PowerSupplyRatio = 0.2;
        Assert.False(dev.Powered);
    }

    [Fact]
    public void DiscardInbound_empties_all_interfaces()
    {
        var dev = new TestDevice(10);
        var i = dev.AddInterface("x");
        i.Inbox.Enqueue(Packet.Create(new Address(1), new Address(10), PacketKind.Heartbeat, ControlTopic.None, Array.Empty<double>(), 1));
        dev.DiscardInbound();
        Assert.Empty(i.Inbox);
    }
}
```

- [ ] **Step 3: Lancer, vérifier l'échec**

Run: `dotnet test --filter "FullyQualifiedName~CableTests|FullyQualifiedName~DeviceTests"`
Expected: FAIL (compilation: `Device`, `Cable`, `NetInterface` introuvables).

- [ ] **Step 4: Implémenter**

`src/Starship.Sim/NetInterface.cs`:

```csharp
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
```

`src/Starship.Sim/Cable.cs`:

```csharp
namespace Starship.Sim;

public sealed class Cable
{
    private sealed record InFlight(NetInterface To, Packet Packet, long ArrivalTick);

    private readonly List<InFlight> _inFlight = new();

    public Cable(string name, double quality, double length)
    {
        Name = name;
        Quality = Math.Clamp(quality, 0, 1);
        Length = Math.Max(0, length);
    }

    public string Name { get; }
    public double Quality { get; }
    public double Length { get; }
    public double Health { get; private set; } = 1.0;
    public bool IsBurnt => Health <= 0;
    public NetInterface? A { get; private set; }
    public NetInterface? B { get; private set; }

    public double ErrorRate => IsBurnt
        ? 1.0
        : Math.Clamp((1 - Quality) * 0.2 + Length * 0.002 + (1 - Health) * 0.3, 0, 1);

    public int LatencyTicks => 1 + (int)(Length / 50.0);

    public void Damage(double amount)
    {
        Health = Math.Max(0, Health - Math.Max(0, amount));
        if (IsBurnt) _inFlight.Clear();
    }

    public void Burn() => Damage(Health);

    public NetInterface Peer(NetInterface from) =>
        from == A ? B! : from == B ? A! : throw new ArgumentException("Interface is not on this cable");

    internal void Attach(NetInterface a, NetInterface b)
    {
        if (A != null) throw new InvalidOperationException($"Cable {Name} is already attached");
        A = a;
        B = b;
        a.Cable = this;
        b.Cable = this;
    }

    internal void Detach()
    {
        if (A != null) A.Cable = null;
        if (B != null) B.Cable = null;
        A = null;
        B = null;
        _inFlight.Clear();
    }

    internal void Transmit(NetInterface from, Packet packet, SimClock clock)
    {
        if (IsBurnt || A == null || B == null) return;
        var to = from == A ? B : A;
        var rate = ErrorRate;
        var rng = clock.Rng;
        if (rng.NextDouble() < rate * 0.5) return;
        if (rng.NextDouble() < rate * 0.5) packet = packet.Corrupted(rng);
        _inFlight.Add(new InFlight(to, packet, clock.Tick + LatencyTicks));
    }

    internal void Step(SimClock clock)
    {
        for (int i = 0; i < _inFlight.Count;)
        {
            var f = _inFlight[i];
            if (f.ArrivalTick <= clock.Tick)
            {
                f.To.Inbox.Enqueue(f.Packet);
                _inFlight.RemoveAt(i);
            }
            else i++;
        }
    }
}
```

`src/Starship.Sim/Device.cs`:

```csharp
namespace Starship.Sim;

public abstract class Device
{
    private readonly List<NetInterface> _interfaces = new();
    private int _seq;

    protected Device(Address address, string name, DeviceKind kind, Address target)
    {
        Address = address;
        Name = name;
        Kind = kind;
        Target = target;
    }

    public Address Address { get; internal set; }
    public string Name { get; }
    public DeviceKind Kind { get; }

    /// <summary>Address this device sends its traffic to (the board computer).</summary>
    public Address Target { get; }

    public IReadOnlyList<NetInterface> Interfaces => _interfaces;
    public double Health { get; set; } = 1.0;

    /// <summary>Electrical demand in watts.</summary>
    public double PowerDemand { get; protected set; }

    /// <summary>Fraction (0..1) of the demanded power actually supplied this tick.</summary>
    public double PowerSupplyRatio { get; internal set; } = 1.0;

    public bool Powered => PowerSupplyRatio >= SimConstants.PowerOffThreshold;

    public NetInterface AddInterface(string name)
    {
        var iface = new NetInterface(this, name);
        _interfaces.Add(iface);
        return iface;
    }

    public void DiscardInbound()
    {
        foreach (var i in _interfaces) i.Inbox.Clear();
    }

    public virtual void Emit(SimClock clock) => EmitHeartbeatAndTelemetry(clock);

    public virtual void Process(SimClock clock) { }

    public abstract TelemetryReport Report();

    protected int NextSeq() => ++_seq;

    /// <summary>Same packet on every interface: redundant cables carry identical copies.</summary>
    protected void Broadcast(SimClock clock, Packet packet)
    {
        foreach (var i in _interfaces) i.Send(packet, clock);
    }

    /// <summary>Under-powered devices stretch their emission period.</summary>
    protected bool IsEmitTick(SimClock clock, int basePeriod)
    {
        var period = (int)Math.Ceiling(basePeriod / Math.Max(PowerSupplyRatio, 0.01));
        return clock.Tick % period == 0;
    }

    protected void EmitHeartbeatAndTelemetry(SimClock clock)
    {
        if (IsEmitTick(clock, SimConstants.HeartbeatPeriod))
            Broadcast(clock, Packet.Create(Address, Target, PacketKind.Heartbeat, ControlTopic.None, Array.Empty<double>(), NextSeq()));
        if (IsEmitTick(clock, SimConstants.TelemetryPeriod))
            Broadcast(clock, Packet.Create(Address, Target, PacketKind.Telemetry, ControlTopic.None, Report().ToPayload(), NextSeq()));
    }
}
```

- [ ] **Step 5: Lancer, vérifier le succès**

Run: `dotnet test --filter "FullyQualifiedName~CableTests|FullyQualifiedName~DeviceTests"`
Expected: PASS. Si `Low_power_slows_emission` échoue, vérifier le calcul de période: `ceil(10/0.5)=20`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: Device, NetInterface, noisy Cable"
```

---

### Task 4: Routeur et gammes

**Files:**
- Create: `src/Starship.Sim/RouterModel.cs`, `src/Starship.Sim/RouterPort.cs`, `src/Starship.Sim/Router.cs`
- Modify: `tests/Starship.Sim.Tests/TestWiring.cs` (ajout de `LinkPort`)
- Test: `tests/Starship.Sim.Tests/RouterTests.cs`

**Interfaces:**
- Consumes: `Device`, `NetInterface`, `Cable`, `Packet`, `SimClock`, `SimConstants`.
- Produces:
  - `record PortSpec(DeviceKind? Dedicated)`; `record RouterCapabilities(bool ConfigurableAddressing, bool RoutingTable, bool Failover, int MaxRoutesPerDst)`; `record RouterModel(string Name, IReadOnlyList<PortSpec> Ports, RouterCapabilities Capabilities, int Throughput, int QueueCapacity)`; `static class RouterModels { Basic, Intermediate, Advanced }`.
  - `RouterPort : NetInterface`: `Router Router`, `int Index`, `PortSpec Spec`, `Address? BoundAddress {get; internal set;}`, `long LastRxTick {get; internal set;}`.
  - `Router(Device owner, RouterModel model)`: `Model`, `IReadOnlyList<RouterPort> Ports`, `Queue<Packet> LocalInbox`, `int DroppedCount`, `void Submit(Packet)`, `void Step(SimClock)`, `bool CanAccept(RouterPort, Device)`, `bool IsUp(RouterPort, SimClock)`, `RouterPort? FindFreePort(DeviceKind)`, `void SetRoute(Address dst, params RouterPort[] ports)`, `void AssignAddress(RouterPort, Address)`, `void DiscardTraffic()`, internes `OnConnected(RouterPort, long tick)`, `OnDisconnected(RouterPort)`.

Gammes: Basique = 4 ports dédiés (Throttle, Joystick, Propulsion, PowerPlant), aucune capacité, débit 8, file 32. Intermédiaire = ces 4 ports dédiés + 4 génériques, `RoutingTable` + `Failover`, 2 routes max, débit 16, file 64. Avancé = 16 ports génériques, toutes capacités, 4 routes max, débit 64, file 128.

- [ ] **Step 1: Ajouter `LinkPort` aux aides de test**

Dans `tests/Starship.Sim.Tests/TestWiring.cs`, ajouter dans la classe:

```csharp
    public static Cable LinkPort(RouterPort port, NetInterface other, SimClock clock, double quality = 1.0, double length = 0.0)
    {
        var cable = Link(port, other, quality, length);
        port.Router.OnConnected(port, clock.Tick);
        return cable;
    }
```

- [ ] **Step 2: Écrire les tests**

`tests/Starship.Sim.Tests/RouterTests.cs`:

```csharp
using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class RouterTests
{
    private static (SimClock clock, TestDevice owner, Router router) Make(RouterModel model)
    {
        var clock = new SimClock(1);
        var owner = new TestDevice(1, DeviceKind.BoardComputer);
        return (clock, owner, new Router(owner, model));
    }

    private static Packet Pkt(int src, int dst, int seq = 1) =>
        Packet.Create(new Address(src), new Address(dst), PacketKind.Control, ControlTopic.Throttle, new[] { 0.5 }, seq);

    private static RouterModel Custom(int ports, RouterCapabilities caps, int throughput = 64, int queue = 64) =>
        new("Custom", Enumerable.Repeat(new PortSpec(null), ports).ToArray(), caps, throughput, queue);

    [Fact]
    public void Gammes_have_expected_port_counts_and_capabilities()
    {
        Assert.Equal(4, RouterModels.Basic.Ports.Count);
        Assert.False(RouterModels.Basic.Capabilities.Failover);
        Assert.Equal(8, RouterModels.Intermediate.Ports.Count);
        Assert.True(RouterModels.Intermediate.Capabilities.Failover);
        Assert.Equal(16, RouterModels.Advanced.Ports.Count);
        Assert.True(RouterModels.Advanced.Capabilities.ConfigurableAddressing);
    }

    [Fact]
    public void Forwards_packet_to_the_port_bound_to_its_destination()
    {
        var (clock, _, router) = Make(RouterModels.Advanced);
        var ia = new TestDevice(10).AddInterface("x");
        var ib = new TestDevice(11).AddInterface("x");
        var ca = TestWiring.LinkPort(router.Ports[0], ia, clock);
        var cb = TestWiring.LinkPort(router.Ports[1], ib, clock);
        router.Ports[0].Inbox.Enqueue(Pkt(10, 11));
        router.Step(clock);
        TestWiring.Pump(clock, ca, cb);
        var got = Assert.Single(ib.Inbox);
        Assert.Equal(11, got.Dst.Value);
        Assert.Empty(ia.Inbox);
    }

    [Fact]
    public void Delivers_packets_for_the_owner_locally()
    {
        var (clock, _, router) = Make(RouterModels.Advanced);
        router.Ports[0].Inbox.Enqueue(Pkt(10, 1));
        router.Step(clock);
        Assert.Single(router.LocalInbox);
    }

    [Fact]
    public void Drops_corrupted_packets()
    {
        var (clock, _, router) = Make(RouterModels.Advanced);
        router.Ports[0].Inbox.Enqueue(Pkt(10, 1).Corrupted(clock.Rng));
        router.Step(clock);
        Assert.Empty(router.LocalInbox);
        Assert.Equal(1, router.DroppedCount);
    }

    [Fact]
    public void Drops_and_counts_packets_for_unknown_destinations()
    {
        var (clock, _, router) = Make(RouterModels.Advanced);
        router.Ports[0].Inbox.Enqueue(Pkt(10, 99));
        var ex = Record.Exception(() => router.Step(clock));
        Assert.Null(ex);
        Assert.Equal(1, router.DroppedCount);
    }

    [Fact]
    public void Throughput_limits_packets_processed_per_tick()
    {
        var (clock, _, router) = Make(Custom(1, new RouterCapabilities(false, false, false, 1), throughput: 2));
        for (int i = 1; i <= 5; i++) router.Submit(Pkt(10, 1, i));
        router.Step(clock);
        Assert.Equal(2, router.LocalInbox.Count);
        router.Step(clock);
        Assert.Equal(4, router.LocalInbox.Count);
    }

    [Fact]
    public void Full_queue_drops_overflow()
    {
        var (_, _, router) = Make(Custom(1, new RouterCapabilities(false, false, false, 1), queue: 3));
        for (int i = 1; i <= 5; i++) router.Submit(Pkt(10, 1, i));
        Assert.Equal(2, router.DroppedCount);
    }

    [Fact]
    public void Dedicated_port_accepts_only_its_device_kind()
    {
        var (_, _, router) = Make(RouterModels.Basic);
        Assert.True(router.CanAccept(router.Ports[0], new TestDevice(10, DeviceKind.Throttle)));
        Assert.False(router.CanAccept(router.Ports[0], new TestDevice(11, DeviceKind.Joystick)));
    }

    [Fact]
    public void FindFreePort_prefers_dedicated_then_generic()
    {
        var (clock, _, router) = Make(RouterModels.Intermediate);
        var first = router.FindFreePort(DeviceKind.Propulsion);
        Assert.Equal(2, first!.Index);
        TestWiring.LinkPort(first, new TestDevice(30, DeviceKind.Propulsion).AddInterface("a"), clock);
        var second = router.FindFreePort(DeviceKind.Propulsion);
        Assert.Equal(4, second!.Index);
    }

    private static (SimClock clock, Router router, NetInterface i1, NetInterface i2, Cable c1, Cable c2) Redundant(RouterModel model)
    {
        var (clock, _, router) = Make(model);
        var dev = new TestDevice(30, DeviceKind.Propulsion);
        var i1 = dev.AddInterface("a");
        var i2 = dev.AddInterface("b");
        var c1 = TestWiring.LinkPort(router.Ports[2], i1, clock);
        var c2 = TestWiring.LinkPort(router.Ports[4], i2, clock);
        return (clock, router, i1, i2, c1, c2);
    }

    [Fact]
    public void With_failover_a_silent_route_is_replaced_by_a_live_one()
    {
        var (clock, router, i1, i2, c1, c2) = Redundant(RouterModels.Intermediate);
        for (int i = 0; i < 100; i++) clock.Advance();
        router.Ports[4].LastRxTick = clock.Tick;   // port 2 stays silent since tick 0
        router.Submit(Pkt(1, 30));
        router.Step(clock);
        TestWiring.Pump(clock, c1, c2);
        Assert.Empty(i1.Inbox);
        Assert.Single(i2.Inbox);
    }

    [Fact]
    public void With_failover_the_first_route_is_used_while_it_is_alive()
    {
        var (clock, router, i1, i2, c1, c2) = Redundant(RouterModels.Intermediate);
        router.Submit(Pkt(1, 30));
        router.Step(clock);
        TestWiring.Pump(clock, c1, c2);
        Assert.Single(i1.Inbox);
        Assert.Empty(i2.Inbox);
    }

    [Fact]
    public void Without_failover_the_first_route_is_kept_even_if_silent()
    {
        var model = Custom(8, new RouterCapabilities(false, false, false, 2));
        var clock = new SimClock(1);
        var router = new Router(new TestDevice(1, DeviceKind.BoardComputer), model);
        var dev = new TestDevice(30, DeviceKind.Propulsion);
        var i1 = dev.AddInterface("a");
        var i2 = dev.AddInterface("b");
        var c1 = TestWiring.LinkPort(router.Ports[0], i1, clock);
        var c2 = TestWiring.LinkPort(router.Ports[1], i2, clock);
        for (int i = 0; i < 100; i++) clock.Advance();
        router.Ports[1].LastRxTick = clock.Tick;
        router.Submit(Pkt(1, 30));
        router.Step(clock);
        TestWiring.Pump(clock, c1, c2);
        Assert.Single(i1.Inbox);
        Assert.Empty(i2.Inbox);
    }

    [Fact]
    public void Receiving_from_the_bound_address_refreshes_the_port()
    {
        var (clock, _, router) = Make(RouterModels.Advanced);
        TestWiring.LinkPort(router.Ports[0], new TestDevice(10).AddInterface("x"), clock);
        for (int i = 0; i < 50; i++) clock.Advance();
        Assert.False(router.IsUp(router.Ports[0], clock));
        router.Ports[0].Inbox.Enqueue(Pkt(10, 1));
        router.Step(clock);
        Assert.True(router.IsUp(router.Ports[0], clock));
    }

    [Fact]
    public void SetRoute_requires_the_capability()
    {
        var (_, _, basic) = Make(RouterModels.Basic);
        Assert.Throws<InvalidOperationException>(() => basic.SetRoute(new Address(30), basic.Ports[0]));
    }

    [Fact]
    public void Explicit_route_takes_priority_over_automatic_order()
    {
        var (clock, router, i1, i2, c1, c2) = Redundant(RouterModels.Intermediate);
        router.SetRoute(new Address(30), router.Ports[4], router.Ports[2]);
        router.Submit(Pkt(1, 30));
        router.Step(clock);
        TestWiring.Pump(clock, c1, c2);
        Assert.Empty(i1.Inbox);
        Assert.Single(i2.Inbox);
    }

    [Fact]
    public void SetRoute_rejects_more_routes_than_the_model_allows()
    {
        var (_, _, router) = Make(RouterModels.Intermediate);
        Assert.Throws<ArgumentException>(() =>
            router.SetRoute(new Address(30), router.Ports[2], router.Ports[4], router.Ports[5]));
    }

    [Fact]
    public void AssignAddress_requires_the_capability()
    {
        var (clock, _, basic) = Make(RouterModels.Basic);
        TestWiring.LinkPort(basic.Ports[0], new TestDevice(10).AddInterface("x"), clock);
        Assert.Throws<InvalidOperationException>(() => basic.AssignAddress(basic.Ports[0], new Address(77)));
    }

    [Fact]
    public void AssignAddress_rebinds_every_port_of_the_device()
    {
        var (clock, _, router) = Make(RouterModels.Advanced);
        var dev = new TestDevice(10);
        TestWiring.LinkPort(router.Ports[0], dev.AddInterface("a"), clock);
        TestWiring.LinkPort(router.Ports[1], dev.AddInterface("b"), clock);
        router.AssignAddress(router.Ports[0], new Address(77));
        Assert.Equal(77, dev.Address.Value);
        Assert.Equal(new Address(77), router.Ports[0].BoundAddress);
        Assert.Equal(new Address(77), router.Ports[1].BoundAddress);
    }

    [Fact]
    public void AssignAddress_needs_a_connected_port()
    {
        var (_, _, router) = Make(RouterModels.Advanced);
        Assert.Throws<InvalidOperationException>(() => router.AssignAddress(router.Ports[0], new Address(77)));
    }

    [Fact]
    public void DiscardTraffic_clears_everything_pending()
    {
        var (clock, _, router) = Make(RouterModels.Advanced);
        router.Ports[0].Inbox.Enqueue(Pkt(10, 1));
        router.Submit(Pkt(10, 1, 2));
        router.Step(clock);
        router.Ports[0].Inbox.Enqueue(Pkt(10, 1, 3));
        router.DiscardTraffic();
        Assert.Empty(router.LocalInbox);
        Assert.Empty(router.Ports[0].Inbox);
        router.Step(clock);
        Assert.Empty(router.LocalInbox);
    }
}
```

- [ ] **Step 3: Lancer, vérifier l'échec**

Run: `dotnet test --filter "FullyQualifiedName~RouterTests"`
Expected: FAIL (compilation: `Router`, `RouterModels` introuvables).

- [ ] **Step 4: Implémenter**

`src/Starship.Sim/RouterModel.cs`:

```csharp
namespace Starship.Sim;

/// <summary>A port is either dedicated to one device kind or generic (null).</summary>
public sealed record PortSpec(DeviceKind? Dedicated);

public sealed record RouterCapabilities(bool ConfigurableAddressing, bool RoutingTable, bool Failover, int MaxRoutesPerDst);

public sealed record RouterModel(
    string Name,
    IReadOnlyList<PortSpec> Ports,
    RouterCapabilities Capabilities,
    int Throughput,
    int QueueCapacity);

public static class RouterModels
{
    private static PortSpec[] DedicatedPorts() => new[]
    {
        new PortSpec(DeviceKind.Throttle),
        new PortSpec(DeviceKind.Joystick),
        new PortSpec(DeviceKind.Propulsion),
        new PortSpec(DeviceKind.PowerPlant),
    };

    public static readonly RouterModel Basic = new(
        "Basic", DedicatedPorts(), new RouterCapabilities(false, false, false, 1), 8, 32);

    public static readonly RouterModel Intermediate = new(
        "Intermediate",
        DedicatedPorts().Concat(Enumerable.Repeat(new PortSpec(null), 4)).ToArray(),
        new RouterCapabilities(false, true, true, 2), 16, 64);

    public static readonly RouterModel Advanced = new(
        "Advanced",
        Enumerable.Repeat(new PortSpec(null), 16).ToArray(),
        new RouterCapabilities(true, true, true, 4), 64, 128);
}
```

`src/Starship.Sim/RouterPort.cs`:

```csharp
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
```

`src/Starship.Sim/Router.cs`:

```csharp
namespace Starship.Sim;

public sealed class Router
{
    private readonly Device _owner;
    private readonly List<RouterPort> _ports;
    private readonly Queue<Packet> _pending = new();
    private readonly Dictionary<Address, List<RouterPort>> _explicitRoutes = new();

    public Router(Device owner, RouterModel model)
    {
        _owner = owner;
        Model = model;
        _ports = model.Ports.Select((spec, i) => new RouterPort(this, owner, i, spec)).ToList();
    }

    public RouterModel Model { get; }
    public IReadOnlyList<RouterPort> Ports => _ports;

    /// <summary>Packets addressed to the owning device (the board computer).</summary>
    public Queue<Packet> LocalInbox { get; } = new();

    public int DroppedCount { get; private set; }

    /// <summary>Entry point for packets emitted by the owner.</summary>
    public void Submit(Packet packet) => Enqueue(packet);

    public void Step(SimClock clock)
    {
        foreach (var port in _ports)
        {
            while (port.Inbox.TryDequeue(out var packet))
            {
                if (!packet.IsValid)
                {
                    DroppedCount++;
                    continue;
                }
                if (port.BoundAddress == packet.Src) port.LastRxTick = clock.Tick;
                Enqueue(packet);
            }
        }

        for (int budget = Model.Throughput; budget > 0 && _pending.TryDequeue(out var next); budget--)
            Forward(next, clock);
    }

    public bool CanAccept(RouterPort port, Device device) =>
        port.Spec.Dedicated == null || port.Spec.Dedicated == device.Kind;

    public bool IsUp(RouterPort port, SimClock clock) =>
        port.IsConnected && clock.Tick - port.LastRxTick <= SimConstants.HeartbeatTimeoutTicks;

    public RouterPort? FindFreePort(DeviceKind kind) =>
        _ports.FirstOrDefault(p => !p.IsConnected && p.Spec.Dedicated == kind)
        ?? _ports.FirstOrDefault(p => !p.IsConnected && p.Spec.Dedicated == null);

    public void SetRoute(Address dst, params RouterPort[] ports)
    {
        if (!Model.Capabilities.RoutingTable)
            throw new InvalidOperationException($"{Model.Name} router has no routing table");
        if (ports.Length == 0 || ports.Length > Model.Capabilities.MaxRoutesPerDst)
            throw new ArgumentException($"{Model.Name} router accepts 1 to {Model.Capabilities.MaxRoutesPerDst} routes per destination");
        _explicitRoutes[dst] = ports.ToList();
    }

    public void AssignAddress(RouterPort port, Address address)
    {
        if (!Model.Capabilities.ConfigurableAddressing)
            throw new InvalidOperationException($"{Model.Name} router does not allow address assignment");
        if (port.Cable == null)
            throw new InvalidOperationException("Port is not connected");
        var peer = port.Cable.Peer(port).Owner;
        peer.Address = address;
        foreach (var p in _ports)
            if (p.Cable != null && p.Cable.Peer(p).Owner == peer) p.BoundAddress = address;
    }

    public void DiscardTraffic()
    {
        foreach (var p in _ports) p.Inbox.Clear();
        _pending.Clear();
        LocalInbox.Clear();
    }

    internal void OnConnected(RouterPort port, long tick)
    {
        port.BoundAddress = port.Cable!.Peer(port).Owner.Address;
        port.LastRxTick = tick;
    }

    internal void OnDisconnected(RouterPort port) => port.BoundAddress = null;

    private void Enqueue(Packet packet)
    {
        if (_pending.Count >= Model.QueueCapacity)
        {
            DroppedCount++;
            return;
        }
        _pending.Enqueue(packet);
    }

    private void Forward(Packet packet, SimClock clock)
    {
        if (packet.Dst == _owner.Address)
        {
            LocalInbox.Enqueue(packet);
            return;
        }
        var port = SelectRoute(packet.Dst, clock);
        if (port == null)
        {
            DroppedCount++;
            return;
        }
        port.Send(packet, clock);
    }

    private RouterPort? SelectRoute(Address dst, SimClock clock)
    {
        var candidates = Candidates(dst);
        if (candidates.Count == 0) return null;
        if (!Model.Capabilities.Failover) return candidates[0];
        return candidates.FirstOrDefault(p => IsUp(p, clock)) ?? candidates[0];
    }

    private List<RouterPort> Candidates(Address dst)
    {
        if (_explicitRoutes.TryGetValue(dst, out var explicitRoute))
            return explicitRoute.Where(p => p.IsConnected).ToList();
        return _ports
            .Where(p => p.IsConnected && p.BoundAddress == dst)
            .Take(Model.Capabilities.MaxRoutesPerDst)
            .ToList();
    }
}
```

- [ ] **Step 5: Lancer, vérifier le succès**

Run: `dotnet test --filter "FullyQualifiedName~RouterTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: Router with model gammes, failover, explicit routes, address assignment"
```

---

### Task 5: ShipBody (physique newtonienne)

**Files:**
- Create: `src/Starship.Sim/ShipBody.cs`
- Test: `tests/Starship.Sim.Tests/ShipBodyTests.cs`

**Interfaces:**
- Produces: `ShipBody(double mass, Vector3 inertia)`: `Mass`, `Inertia` (moments principaux, kg·m²), `Vector3 Position`, `Vector3 Velocity`, `Quaternion Orientation`, `Vector3 AngularVelocity {get; set;}` (repère du vaisseau), `void Step(Vector3 forceBody, Vector3 torqueBody, double dt)`. Axe avant du vaisseau = +Z. Pas de couplage gyroscopique ni d'amortissement.

- [ ] **Step 1: Écrire les tests**

`tests/Starship.Sim.Tests/ShipBodyTests.cs`:

```csharp
using System.Numerics;
using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class ShipBodyTests
{
    private const double Dt = SimClock.Dt;

    private static void Run(ShipBody body, Vector3 force, Vector3 torque, int ticks)
    {
        for (int i = 0; i < ticks; i++) body.Step(force, torque, Dt);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, float tol)
    {
        Assert.InRange(actual.X, expected.X - tol, expected.X + tol);
        Assert.InRange(actual.Y, expected.Y - tol, expected.Y + tol);
        Assert.InRange(actual.Z, expected.Z - tol, expected.Z + tol);
    }

    [Fact]
    public void Constant_force_gives_expected_velocity_and_position()
    {
        var body = new ShipBody(10, Vector3.One);
        Run(body, new Vector3(0, 0, 100), Vector3.Zero, 60);
        AssertNear(new Vector3(0, 0, 10), body.Velocity, 0.01f);
        Assert.InRange(body.Position.Z, 5.0f, 5.2f);
    }

    [Fact]
    public void Velocity_persists_without_thrust()
    {
        var body = new ShipBody(10, Vector3.One);
        Run(body, new Vector3(0, 0, 100), Vector3.Zero, 60);
        var v = body.Velocity;
        Run(body, Vector3.Zero, Vector3.Zero, 300);
        Assert.Equal(v, body.Velocity);
    }

    [Fact]
    public void Torque_spins_up_angular_velocity()
    {
        var body = new ShipBody(10, new Vector3(10, 10, 10));
        Run(body, Vector3.Zero, new Vector3(0, 10, 0), 60);
        AssertNear(new Vector3(0, 1, 0), body.AngularVelocity, 0.01f);
    }

    [Fact]
    public void Angular_velocity_persists_without_torque()
    {
        var body = new ShipBody(10, Vector3.One) { AngularVelocity = new Vector3(0, 0.5f, 0) };
        Run(body, Vector3.Zero, Vector3.Zero, 600);
        AssertNear(new Vector3(0, 0.5f, 0), body.AngularVelocity, 1e-6f);
    }

    [Fact]
    public void Thrust_follows_orientation()
    {
        // quarter turn about body Y: forward (+Z) becomes world +X
        var body = new ShipBody(10, Vector3.One) { AngularVelocity = new Vector3(0, MathF.PI / 2, 0) };
        Run(body, Vector3.Zero, Vector3.Zero, 60);
        body.AngularVelocity = Vector3.Zero;
        Run(body, new Vector3(0, 0, 100), Vector3.Zero, 60);
        AssertNear(new Vector3(10, 0, 0), body.Velocity, 0.05f);
    }

    [Fact]
    public void Rotations_compose_in_the_body_frame()
    {
        var body = new ShipBody(10, Vector3.One) { AngularVelocity = new Vector3(MathF.PI / 2, 0, 0) };
        Run(body, Vector3.Zero, Vector3.Zero, 60);          // 90° about body X: forward -> world -Y
        body.AngularVelocity = new Vector3(0, MathF.PI / 2, 0);
        Run(body, Vector3.Zero, Vector3.Zero, 60);          // 90° about body Y (now world +Z)
        var forward = Vector3.Transform(Vector3.UnitZ, body.Orientation);
        AssertNear(new Vector3(1, 0, 0), forward, 1e-3f);
    }
}
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `dotnet test --filter "FullyQualifiedName~ShipBodyTests"`
Expected: FAIL (compilation: `ShipBody` introuvable).

- [ ] **Step 3: Implémenter**

`src/Starship.Sim/ShipBody.cs`:

```csharp
using System.Numerics;

namespace Starship.Sim;

/// <summary>
/// Rigid body, semi-implicit Euler. Forward is +Z in the body frame.
/// Angular velocity is expressed in the body frame; no gyroscopic coupling, no damping.
/// </summary>
public sealed class ShipBody
{
    public ShipBody(double mass, Vector3 inertia)
    {
        Mass = mass;
        Inertia = inertia;
    }

    public double Mass { get; }
    public Vector3 Inertia { get; }
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public Quaternion Orientation { get; private set; } = Quaternion.Identity;
    public Vector3 AngularVelocity { get; set; }

    public void Step(Vector3 forceBody, Vector3 torqueBody, double dt)
    {
        var dtf = (float)dt;
        var forceWorld = Vector3.Transform(forceBody, Orientation);
        Velocity += forceWorld / (float)Mass * dtf;
        Position += Velocity * dtf;

        AngularVelocity += torqueBody / Inertia * dtf;
        var rotation = AngularVelocity * dtf;
        var angle = rotation.Length();
        if (angle > 1e-9f)
        {
            var delta = Quaternion.CreateFromAxisAngle(rotation / angle, angle);
            Orientation = Quaternion.Normalize(Orientation * delta);
        }
    }
}
```

- [ ] **Step 4: Lancer, vérifier le succès**

Run: `dotnet test --filter "FullyQualifiedName~ShipBodyTests"`
Expected: PASS. Si `Rotations_compose_in_the_body_frame` échoue, l'ordre `Orientation * delta` est inversé pour `System.Numerics`: utiliser `Quaternion.Concatenate(delta, Orientation)` et relancer les 6 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: ShipBody newtonian 3D physics"
```

---

### Task 6: Ship (topologie éditable)

**Files:**
- Create: `src/Starship.Sim/Ship.cs`
- Test: `tests/Starship.Sim.Tests/ShipTopologyTests.cs`

**Interfaces:**
- Consumes: `Device`, `Cable`, `NetInterface`, `RouterPort`, `Router`, `ShipBody`, `SimClock`.
- Produces: `Ship(int seed, ShipBody body)`: `SimClock Clock`, `ShipBody Body`, `IReadOnlyList<Device> Devices`, `IReadOnlyList<Cable> Cables`, `T Add<T>(T device) where T : Device` (adresse unique, sinon `ArgumentException`), `Cable Connect(NetInterface a, NetInterface b, Cable cable)` (`InvalidOperationException` si interface déjà branchée, même appareil, ou port dédié refusant le type), `void Disconnect(Cable)`, `Cable Replace(Cable oldCable, Cable newCable)`. `Step()` est ajouté en Task 11.

- [ ] **Step 1: Écrire les tests**

`tests/Starship.Sim.Tests/ShipTopologyTests.cs`:

```csharp
using System.Numerics;
using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class ShipTopologyTests
{
    private static (Ship ship, Router router) Make(RouterModel model)
    {
        var ship = new Ship(1, new ShipBody(1000, Vector3.One));
        var owner = ship.Add(new TestDevice(1, DeviceKind.BoardComputer));
        return (ship, new Router(owner, model));
    }

    [Fact]
    public void Connect_attaches_the_cable_and_binds_the_router_port()
    {
        var (ship, router) = Make(RouterModels.Basic);
        var throttle = ship.Add(new TestDevice(10, DeviceKind.Throttle));
        var iface = throttle.AddInterface("out");
        var cable = ship.Connect(router.Ports[0], iface, new Cable("c", 1, 2));
        Assert.Same(cable, iface.Cable);
        Assert.Same(cable, router.Ports[0].Cable);
        Assert.Equal(new Address(10), router.Ports[0].BoundAddress);
        Assert.Contains(cable, ship.Cables);
    }

    [Fact]
    public void Dedicated_port_refuses_the_wrong_device_kind()
    {
        var (ship, router) = Make(RouterModels.Basic);
        var joystick = ship.Add(new TestDevice(11, DeviceKind.Joystick));
        var iface = joystick.AddInterface("out");
        Assert.Throws<InvalidOperationException>(() => ship.Connect(router.Ports[0], iface, new Cable("c", 1, 2)));
        Assert.Null(iface.Cable);
        Assert.Null(router.Ports[0].BoundAddress);
    }

    [Fact]
    public void Connecting_an_already_connected_interface_is_refused()
    {
        var (ship, router) = Make(RouterModels.Advanced);
        var dev = ship.Add(new TestDevice(10));
        var iface = dev.AddInterface("out");
        ship.Connect(router.Ports[0], iface, new Cable("a", 1, 1));
        Assert.Throws<InvalidOperationException>(() => ship.Connect(router.Ports[1], iface, new Cable("b", 1, 1)));
    }

    [Fact]
    public void Connecting_two_interfaces_of_the_same_device_is_refused()
    {
        var (ship, _) = Make(RouterModels.Advanced);
        var dev = ship.Add(new TestDevice(10));
        var a = dev.AddInterface("a");
        var b = dev.AddInterface("b");
        Assert.Throws<InvalidOperationException>(() => ship.Connect(a, b, new Cable("loop", 1, 1)));
    }

    [Fact]
    public void Disconnect_frees_both_ends()
    {
        var (ship, router) = Make(RouterModels.Advanced);
        var dev = ship.Add(new TestDevice(10));
        var iface = dev.AddInterface("out");
        var cable = ship.Connect(router.Ports[0], iface, new Cable("c", 1, 1));
        ship.Disconnect(cable);
        Assert.Null(iface.Cable);
        Assert.Null(router.Ports[0].Cable);
        Assert.Null(router.Ports[0].BoundAddress);
        Assert.DoesNotContain(cable, ship.Cables);
    }

    [Fact]
    public void Replace_swaps_the_cable_and_gives_the_new_route_a_fresh_grace_period()
    {
        var (ship, router) = Make(RouterModels.Advanced);
        var dev = ship.Add(new TestDevice(10));
        var iface = dev.AddInterface("out");
        var old = ship.Connect(router.Ports[0], iface, new Cable("old", 1, 1));
        old.Burn();
        for (int i = 0; i < 100; i++) ship.Clock.Advance();
        var fresh = ship.Replace(old, new Cable("new", 1, 1));
        Assert.Same(fresh, iface.Cable);
        Assert.Null(old.A);
        Assert.Equal(ship.Clock.Tick, router.Ports[0].LastRxTick);
        Assert.True(router.IsUp(router.Ports[0], ship.Clock));
    }

    [Fact]
    public void Duplicate_addresses_are_refused()
    {
        var (ship, _) = Make(RouterModels.Basic);
        ship.Add(new TestDevice(10));
        Assert.Throws<ArgumentException>(() => ship.Add(new TestDevice(10)));
    }
}
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `dotnet test --filter "FullyQualifiedName~ShipTopologyTests"`
Expected: FAIL (compilation: `Ship` introuvable).

- [ ] **Step 3: Implémenter**

`src/Starship.Sim/Ship.cs`:

```csharp
namespace Starship.Sim;

public sealed class Ship
{
    private readonly List<Device> _devices = new();
    private readonly List<Cable> _cables = new();

    public Ship(int seed, ShipBody body)
    {
        Clock = new SimClock(seed);
        Body = body;
    }

    public SimClock Clock { get; }
    public ShipBody Body { get; }
    public IReadOnlyList<Device> Devices => _devices;
    public IReadOnlyList<Cable> Cables => _cables;

    public T Add<T>(T device) where T : Device
    {
        if (_devices.Any(d => d.Address == device.Address))
            throw new ArgumentException($"Address {device.Address} is already used by another device");
        _devices.Add(device);
        return device;
    }

    public Cable Connect(NetInterface a, NetInterface b, Cable cable)
    {
        if (a.Cable != null || b.Cable != null)
            throw new InvalidOperationException("Interface is already connected");
        if (a.Owner == b.Owner)
            throw new InvalidOperationException("Cannot connect a device to itself");
        EnsureAccepted(a, b);
        EnsureAccepted(b, a);

        cable.Attach(a, b);
        if (!_cables.Contains(cable)) _cables.Add(cable);
        if (a is RouterPort pa) pa.Router.OnConnected(pa, Clock.Tick);
        if (b is RouterPort pb) pb.Router.OnConnected(pb, Clock.Tick);
        return cable;
    }

    public void Disconnect(Cable cable)
    {
        var a = cable.A ?? throw new InvalidOperationException("Cable is not connected");
        var b = cable.B!;
        cable.Detach();
        _cables.Remove(cable);
        if (a is RouterPort pa) pa.Router.OnDisconnected(pa);
        if (b is RouterPort pb) pb.Router.OnDisconnected(pb);
    }

    public Cable Replace(Cable oldCable, Cable newCable)
    {
        var a = oldCable.A ?? throw new InvalidOperationException("Cable is not connected");
        var b = oldCable.B!;
        Disconnect(oldCable);
        return Connect(a, b, newCable);
    }

    private static void EnsureAccepted(NetInterface iface, NetInterface other)
    {
        if (iface is RouterPort port && !port.Router.CanAccept(port, other.Owner))
            throw new InvalidOperationException(
                $"Port {port.Index} of the {port.Router.Model.Name} router does not accept a {other.Owner.Kind}");
    }
}
```

- [ ] **Step 4: Lancer, vérifier le succès**

Run: `dotnet test --filter "FullyQualifiedName~ShipTopologyTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: Ship topology (add, connect, disconnect, replace)"
```

---

### Task 7: ThrottleLever et Joystick

**Files:**
- Create: `src/Starship.Sim/ThrottleLever.cs`, `src/Starship.Sim/Joystick.cs`
- Test: `tests/Starship.Sim.Tests/CockpitTests.cs`

**Interfaces:**
- Consumes: `Device`, `Packet`, `SimConstants`.
- Produces: `ThrottleLever(Address address, Address computer)`: `NetInterface Out`, `double Position` (0..1), `void Set(double)` (borné). Émet `Control/Throttle` avec `Payload = [position]` toutes les `ControlPeriod` ticks. `Joystick(Address address, Address computer)`: `NetInterface Out`, `double Pitch, Roll, Yaw` (-1..1), `void Set(double pitch, double roll, double yaw)` (borné). Émet `Control/Stick` avec `Payload = [pitch, roll, yaw]`. Les deux appellent aussi `base.Emit` (heartbeat + télémétrie). Consommation 5 W chacun.

- [ ] **Step 1: Écrire les tests**

`tests/Starship.Sim.Tests/CockpitTests.cs`:

```csharp
using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class CockpitTests
{
    private static (SimClock clock, NetInterface peer, Cable cable, ThrottleLever throttle) Plug()
    {
        var clock = new SimClock(1);
        var throttle = new ThrottleLever(new Address(10), new Address(1));
        var peer = new TestDevice(1).AddInterface("x");
        var cable = TestWiring.Link(throttle.Out, peer);
        return (clock, peer, cable, throttle);
    }

    [Theory]
    [InlineData(0.4, 0.4)]
    [InlineData(5.0, 1.0)]
    [InlineData(-2.0, 0.0)]
    public void Throttle_position_is_clamped(double input, double expected)
    {
        var (_, _, _, throttle) = Plug();
        throttle.Set(input);
        Assert.Equal(expected, throttle.Position);
    }

    [Fact]
    public void Throttle_ignores_non_finite_input()
    {
        var (_, _, _, throttle) = Plug();
        throttle.Set(0.5);
        throttle.Set(double.NaN);
        Assert.Equal(0.5, throttle.Position);
    }

    [Fact]
    public void Throttle_emits_its_position_on_the_control_period()
    {
        var (clock, peer, cable, throttle) = Plug();
        throttle.Set(0.7);
        throttle.Emit(clock);                       // tick 0
        TestWiring.Pump(clock, cable);              // -> tick 1
        throttle.Emit(clock);                       // tick 1: no control packet
        TestWiring.Pump(clock, cable);
        var controls = peer.Inbox.Where(p => p.Kind == PacketKind.Control).ToList();
        var packet = Assert.Single(controls);
        Assert.Equal(ControlTopic.Throttle, packet.Topic);
        Assert.Equal(10, packet.Src.Value);
        Assert.Equal(1, packet.Dst.Value);
        Assert.Equal(0.7, packet.Payload[0]);
    }

    [Fact]
    public void Low_power_slows_the_control_cadence()
    {
        int Count(double ratio)
        {
            var (clock, peer, cable, throttle) = Plug();
            throttle.PowerSupplyRatio = ratio;
            for (int i = 0; i < 8; i++)
            {
                throttle.Emit(clock);
                TestWiring.Pump(clock, cable);
            }
            return peer.Inbox.Count(p => p.Kind == PacketKind.Control);
        }

        Assert.Equal(4, Count(1.0));   // ticks 0, 2, 4, 6
        Assert.Equal(2, Count(0.5));   // period 4: ticks 0, 4
    }

    [Fact]
    public void Unplugged_throttle_emits_without_error()
    {
        var throttle = new ThrottleLever(new Address(10), new Address(1));
        var ex = Record.Exception(() => throttle.Emit(new SimClock(1)));
        Assert.Null(ex);
    }

    [Fact]
    public void Joystick_clamps_and_emits_three_axes()
    {
        var clock = new SimClock(1);
        var stick = new Joystick(new Address(11), new Address(1));
        var peer = new TestDevice(1).AddInterface("x");
        var cable = TestWiring.Link(stick.Out, peer);
        stick.Set(0.5, -3.0, 2.0);
        Assert.Equal(-1.0, stick.Roll);
        Assert.Equal(1.0, stick.Yaw);
        stick.Emit(clock);
        TestWiring.Pump(clock, cable);
        var packet = peer.Inbox.Single(p => p.Kind == PacketKind.Control);
        Assert.Equal(ControlTopic.Stick, packet.Topic);
        Assert.Equal(new[] { 0.5, -1.0, 1.0 }, packet.Payload);
    }

    [Fact]
    public void Joystick_ignores_non_finite_input()
    {
        var stick = new Joystick(new Address(11), new Address(1));
        stick.Set(0.2, 0.2, 0.2);
        stick.Set(double.NaN, 0.9, 0.9);
        Assert.Equal(0.2, stick.Pitch);
        Assert.Equal(0.2, stick.Roll);
    }
}
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `dotnet test --filter "FullyQualifiedName~CockpitTests"`
Expected: FAIL (compilation).

- [ ] **Step 3: Implémenter**

`src/Starship.Sim/ThrottleLever.cs`:

```csharp
namespace Starship.Sim;

public sealed class ThrottleLever : Device
{
    public ThrottleLever(Address address, Address computer)
        : base(address, "Throttle", DeviceKind.Throttle, computer)
    {
        Out = AddInterface("out");
        PowerDemand = 5;
    }

    public NetInterface Out { get; }

    /// <summary>Lever position, 0 (idle) to 1 (full power).</summary>
    public double Position { get; private set; }

    public void Set(double position)
    {
        if (!double.IsFinite(position)) return;
        Position = Math.Clamp(position, 0, 1);
    }

    public override void Emit(SimClock clock)
    {
        if (IsEmitTick(clock, SimConstants.ControlPeriod))
            Broadcast(clock, Packet.Create(Address, Target, PacketKind.Control, ControlTopic.Throttle, new[] { Position }, NextSeq()));
        base.Emit(clock);
    }

    public override TelemetryReport Report() =>
        new(Kind, PowerDemand * PowerSupplyRatio, 20, Position, Health);
}
```

`src/Starship.Sim/Joystick.cs`:

```csharp
namespace Starship.Sim;

public sealed class Joystick : Device
{
    public Joystick(Address address, Address computer)
        : base(address, "Joystick", DeviceKind.Joystick, computer)
    {
        Out = AddInterface("out");
        PowerDemand = 5;
    }

    public NetInterface Out { get; }
    public double Pitch { get; private set; }
    public double Roll { get; private set; }
    public double Yaw { get; private set; }

    public void Set(double pitch, double roll, double yaw)
    {
        if (!double.IsFinite(pitch) || !double.IsFinite(roll) || !double.IsFinite(yaw)) return;
        Pitch = Math.Clamp(pitch, -1, 1);
        Roll = Math.Clamp(roll, -1, 1);
        Yaw = Math.Clamp(yaw, -1, 1);
    }

    public override void Emit(SimClock clock)
    {
        if (IsEmitTick(clock, SimConstants.ControlPeriod))
            Broadcast(clock, Packet.Create(Address, Target, PacketKind.Control, ControlTopic.Stick, new[] { Pitch, Roll, Yaw }, NextSeq()));
        base.Emit(clock);
    }

    public override TelemetryReport Report() =>
        new(Kind, PowerDemand * PowerSupplyRatio, 20, Math.Max(Math.Abs(Pitch), Math.Max(Math.Abs(Roll), Math.Abs(Yaw))), Health);
}
```

- [ ] **Step 4: Lancer, vérifier le succès**

Run: `dotnet test --filter "FullyQualifiedName~CockpitTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: ThrottleLever and Joystick emitting control packets"
```

---

### Task 8: PowerPlant et répartition d'énergie

**Files:**
- Create: `src/Starship.Sim/PowerPlant.cs`, `src/Starship.Sim/PowerDistribution.cs`
- Test: `tests/Starship.Sim.Tests/PowerTests.cs`

**Interfaces:**
- Consumes: `Device`, `TestDevice.SetDemand` (tests).
- Produces: `PowerPlant(Address address, Address computer, double capacity)`: `NetInterface Out`, `double Capacity`, `bool Online {get;set;}` (true par défaut), `double Available` (`Online ? Capacity : 0`), `double CurrentDemand {get; internal set;}`. `static class PowerDistribution { static void Apply(PowerPlant? plant, IEnumerable<Device> devices) }`: tous les appareils sauf le groupe reçoivent le même `PowerSupplyRatio = min(1, Available / somme des demandes)`; sans groupe, ratio 0.

- [ ] **Step 1: Écrire les tests**

`tests/Starship.Sim.Tests/PowerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `dotnet test --filter "FullyQualifiedName~PowerTests"`
Expected: FAIL (compilation).

- [ ] **Step 3: Implémenter**

`src/Starship.Sim/PowerPlant.cs`:

```csharp
namespace Starship.Sim;

public sealed class PowerPlant : Device
{
    public PowerPlant(Address address, Address computer, double capacity)
        : base(address, "PowerPlant", DeviceKind.PowerPlant, computer)
    {
        Capacity = capacity;
        Out = AddInterface("out");
    }

    public NetInterface Out { get; }
    public double Capacity { get; }
    public bool Online { get; set; } = true;
    public double Available => Online ? Capacity : 0;
    public double CurrentDemand { get; internal set; }

    private double Load => Capacity > 0 ? Math.Min(1.0, CurrentDemand / Capacity) : 0;

    public override TelemetryReport Report() =>
        new(Kind, CurrentDemand, 20 + 60 * Load, Load, Health);
}
```

`src/Starship.Sim/PowerDistribution.cs`:

```csharp
namespace Starship.Sim;

public static class PowerDistribution
{
    public static void Apply(PowerPlant? plant, IEnumerable<Device> devices)
    {
        var consumers = devices.Where(d => d != plant).ToList();
        var demand = consumers.Sum(d => d.PowerDemand);
        var available = plant?.Available ?? 0;
        var ratio = demand <= 0 ? 1.0 : Math.Min(1.0, available / demand);
        foreach (var d in consumers) d.PowerSupplyRatio = ratio;
        if (plant != null) plant.CurrentDemand = demand;
    }
}
```

- [ ] **Step 4: Lancer, vérifier le succès**

Run: `dotnet test --filter "FullyQualifiedName~PowerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: PowerPlant and proportional power distribution"
```

---

### Task 9: BoardComputer

**Files:**
- Create: `src/Starship.Sim/BoardComputer.cs`
- Test: `tests/Starship.Sim.Tests/BoardComputerTests.cs`

**Interfaces:**
- Consumes: `Router`, `RouterModel`, `Packet`, `TelemetryReport`, `Device`, `SimConstants`.
- Produces: `BoardComputer(Address address, RouterModel model)`: `Router Router`, `double CommandedThrottle`, `(double Pitch, double Roll, double Yaw) CommandedStick`, `IReadOnlyDictionary<Address, DeviceStatus> Status`, `bool IsStale(Address, SimClock)`, `long AgeOf(Address, SimClock)`; `record DeviceStatus(TelemetryReport Report, long LastTelemetryTick)`. `Process` vide `Router.LocalInbox` (déduplication par `Seq` croissant par source, rejet des payloads invalides) puis envoie un paquet `Control/Setpoint` `[throttle, pitch, roll, yaw]` à la propulsion connue (appareil dont la télémétrie a `Kind == Propulsion`) tant que la commande de poussée a moins de `ControlTimeoutTicks` ticks (manche périmé = neutre). `Emit` ne fait rien. Consommation 30 W.

- [ ] **Step 1: Écrire les tests**

`tests/Starship.Sim.Tests/BoardComputerTests.cs`:

```csharp
using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class BoardComputerTests
{
    private static BoardComputer Make() => new(new Address(1), RouterModels.Advanced);

    private static Packet Control(int src, ControlTopic topic, int seq, params double[] payload) =>
        Packet.Create(new Address(src), new Address(1), PacketKind.Control, topic, payload, seq);

    private static Packet Telemetry(int src, DeviceKind kind, int seq) =>
        Packet.Create(new Address(src), new Address(1), PacketKind.Telemetry, ControlTopic.None,
            new TelemetryReport(kind, 10, 20, 0.1, 1).ToPayload(), seq);

    [Fact]
    public void Applies_throttle_and_stick_commands()
    {
        var pc = Make();
        var clock = new SimClock(1);
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 0.6));
        pc.Router.LocalInbox.Enqueue(Control(11, ControlTopic.Stick, 1, 0.1, -0.2, 0.3));
        pc.Process(clock);
        Assert.Equal(0.6, pc.CommandedThrottle);
        Assert.Equal((0.1, -0.2, 0.3), pc.CommandedStick);
    }

    [Fact]
    public void Duplicate_and_older_sequence_numbers_are_ignored()
    {
        var pc = Make();
        var clock = new SimClock(1);
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 5, 0.3));
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 5, 0.9));   // redundant copy
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 4, 0.8));   // late copy
        pc.Process(clock);
        Assert.Equal(0.3, pc.CommandedThrottle);
    }

    [Fact]
    public void Out_of_range_throttle_is_clamped()
    {
        var pc = Make();
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 5.0));
        pc.Process(new SimClock(1));
        Assert.Equal(1.0, pc.CommandedThrottle);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_finite_commands_are_ignored(double value)
    {
        var pc = Make();
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 0.4));
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 2, value));
        pc.Process(new SimClock(1));
        Assert.Equal(0.4, pc.CommandedThrottle);
    }

    [Fact]
    public void Commands_with_the_wrong_payload_length_are_ignored()
    {
        var pc = Make();
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 0.1, 0.2));
        pc.Router.LocalInbox.Enqueue(Control(11, ControlTopic.Stick, 1, 0.1));
        pc.Process(new SimClock(1));
        Assert.Equal(0.0, pc.CommandedThrottle);
        Assert.Equal((0.0, 0.0, 0.0), pc.CommandedStick);
    }

    [Fact]
    public void Telemetry_updates_status_and_goes_stale()
    {
        var pc = Make();
        var clock = new SimClock(1);
        pc.Router.LocalInbox.Enqueue(Telemetry(20, DeviceKind.PowerPlant, 1));
        pc.Process(clock);
        var status = pc.Status[new Address(20)];
        Assert.Equal(DeviceKind.PowerPlant, status.Report.Kind);
        for (int i = 0; i < 60; i++) clock.Advance();
        Assert.False(pc.IsStale(new Address(20), clock));
        clock.Advance();
        Assert.True(pc.IsStale(new Address(20), clock));
        Assert.Equal(61, pc.AgeOf(new Address(20), clock));
    }

    [Fact]
    public void Unknown_device_counts_as_stale()
    {
        Assert.True(Make().IsStale(new Address(99), new SimClock(1)));
    }

    [Fact]
    public void Malformed_telemetry_is_ignored()
    {
        var pc = Make();
        var bad = Packet.Create(new Address(20), new Address(1), PacketKind.Telemetry, ControlTopic.None, new[] { 1.0, 2.0 }, 1);
        pc.Router.LocalInbox.Enqueue(bad);
        pc.Process(new SimClock(1));
        Assert.Empty(pc.Status);
    }

    private static (BoardComputer pc, SimClock clock, NetInterface propulsion, Cable cable) WithPropulsion()
    {
        var clock = new SimClock(1);
        var pc = Make();
        var iface = new TestDevice(30, DeviceKind.Propulsion).AddInterface("in");
        var cable = TestWiring.LinkPort(pc.Router.Ports[0], iface, clock);
        pc.Router.LocalInbox.Enqueue(Telemetry(30, DeviceKind.Propulsion, 1));
        return (pc, clock, iface, cable);
    }

    [Fact]
    public void Sends_a_setpoint_to_the_propulsion()
    {
        var (pc, clock, propulsion, cable) = WithPropulsion();
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 0.6));
        pc.Router.LocalInbox.Enqueue(Control(11, ControlTopic.Stick, 1, 0.1, 0.2, 0.3));
        pc.Process(clock);
        pc.Router.Step(clock);
        TestWiring.Pump(clock, cable);
        var sp = Assert.Single(propulsion.Inbox);
        Assert.Equal(ControlTopic.Setpoint, sp.Topic);
        Assert.Equal(1, sp.Src.Value);
        Assert.Equal(30, sp.Dst.Value);
        Assert.Equal(new[] { 0.6, 0.1, 0.2, 0.3 }, sp.Payload);
    }

    [Fact]
    public void No_setpoint_without_a_known_propulsion()
    {
        var clock = new SimClock(1);
        var pc = Make();
        var iface = new TestDevice(30, DeviceKind.Propulsion).AddInterface("in");
        var cable = TestWiring.LinkPort(pc.Router.Ports[0], iface, clock);
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 0.6));
        pc.Process(clock);
        pc.Router.Step(clock);
        TestWiring.Pump(clock, cable);
        Assert.Empty(iface.Inbox);
    }

    [Fact]
    public void Stale_throttle_stops_the_setpoints()
    {
        var (pc, clock, propulsion, cable) = WithPropulsion();
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 0.6));
        pc.Process(clock);                                   // tick 0: command received
        for (int i = 0; i < 32; i++) clock.Advance();        // tick 32: command is 32 ticks old
        pc.Process(clock);
        pc.Router.Step(clock);
        TestWiring.Pump(clock, cable);
        Assert.DoesNotContain(propulsion.Inbox, p => p.Topic == ControlTopic.Setpoint && p.Seq > 1);
    }

    [Fact]
    public void Stale_stick_falls_back_to_neutral_while_throttle_is_fresh()
    {
        var (pc, clock, propulsion, cable) = WithPropulsion();
        pc.Router.LocalInbox.Enqueue(Control(11, ControlTopic.Stick, 1, 1, 1, 1));
        pc.Process(clock);                                   // tick 0: stick received, no throttle yet
        for (int i = 0; i < 32; i++) clock.Advance();        // tick 32
        pc.Router.LocalInbox.Enqueue(Control(10, ControlTopic.Throttle, 1, 0.5));
        pc.Process(clock);
        pc.Router.Step(clock);
        TestWiring.Pump(clock, cable);
        var sp = propulsion.Inbox.Last(p => p.Topic == ControlTopic.Setpoint);
        Assert.Equal(new[] { 0.5, 0.0, 0.0, 0.0 }, sp.Payload);
    }

    [Fact]
    public void Board_computer_does_not_emit_network_traffic_on_its_own()
    {
        var pc = Make();
        var ex = Record.Exception(() => pc.Emit(new SimClock(1)));
        Assert.Null(ex);
        Assert.Empty(pc.Interfaces);
    }
}
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `dotnet test --filter "FullyQualifiedName~BoardComputerTests"`
Expected: FAIL (compilation).

- [ ] **Step 3: Implémenter**

`src/Starship.Sim/BoardComputer.cs`:

```csharp
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
        var propulsion = _status.FirstOrDefault(kv => kv.Value.Report.Kind == DeviceKind.Propulsion);
        if (propulsion.Value == null) return;

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
```

- [ ] **Step 4: Lancer, vérifier le succès**

Run: `dotnet test --filter "FullyQualifiedName~BoardComputerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: BoardComputer with telemetry table and setpoint generation"
```

---

### Task 10: PropulsionSystem

**Files:**
- Create: `src/Starship.Sim/PropulsionSystem.cs`
- Test: `tests/Starship.Sim.Tests/PropulsionTests.cs`

**Interfaces:**
- Consumes: `Device`, `Packet`, `SimConstants`.
- Produces: `PropulsionSystem(Address address, Address computer, double maxThrust, Vector3 maxTorque)`: `NetInterface In`, `double MaxThrust`, `Vector3 MaxTorque`; `Process` lit toutes ses interfaces et applique les paquets `Control/Setpoint` valides (checksum, destinataire, `Seq` croissant, 4 valeurs finies; throttle borné [0,1], manche borné [-1,1]); `(Vector3 Force, Vector3 Torque) Output(SimClock)`: poussée `+Z = throttle × MaxThrust × PowerSupplyRatio`, couple `(pitch, yaw, roll) × MaxTorque × ratio`, nuls si non alimenté (`!Powered`) ou si la dernière consigne a plus de `SetpointHoldTicks` ticks. Demande d'énergie = `20 + 200 × throttle` (throttle effectif).

- [ ] **Step 1: Écrire les tests**

`tests/Starship.Sim.Tests/PropulsionTests.cs`:

```csharp
using System.Numerics;
using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class PropulsionTests
{
    private static PropulsionSystem Make() =>
        new(new Address(30), new Address(1), 1000, new Vector3(100, 200, 300));

    private static Packet Setpoint(int seq, params double[] payload) =>
        Packet.Create(new Address(1), new Address(30), PacketKind.Control, ControlTopic.Setpoint, payload, seq);

    private static void Feed(PropulsionSystem p, Packet packet, SimClock clock)
    {
        p.In.Inbox.Enqueue(packet);
        p.Process(clock);
    }

    [Fact]
    public void Thrust_acts_along_forward_axis_scaled_by_throttle()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 0.5, 0, 0, 0), clock);
        var (force, torque) = p.Output(clock);
        Assert.Equal(new Vector3(0, 0, 500), force);
        Assert.Equal(Vector3.Zero, torque);
    }

    [Fact]
    public void Stick_maps_pitch_to_x_yaw_to_y_roll_to_z()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 0, 0.5, 1, -1), clock);   // pitch .5, roll 1, yaw -1
        var (_, torque) = p.Output(clock);
        Assert.Equal(new Vector3(50, -200, 300), torque);
    }

    [Fact]
    public void Setpoint_is_held_then_cut()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 1, 0, 0, 0), clock);       // tick 0
        for (int i = 0; i < 30; i++) clock.Advance();
        Assert.NotEqual(Vector3.Zero, p.Output(clock).Force);    // tick 30: still held
        clock.Advance();
        Assert.Equal(Vector3.Zero, p.Output(clock).Force);       // tick 31: cut
    }

    [Fact]
    public void Without_any_setpoint_there_is_no_thrust()
    {
        var clock = new SimClock(1);
        Assert.Equal((Vector3.Zero, Vector3.Zero), Make().Output(clock));
    }

    [Fact]
    public void Power_ratio_scales_output_and_low_power_cuts_it()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 1, 0, 0, 0), clock);
        p.PowerSupplyRatio = 0.5;
        Assert.Equal(new Vector3(0, 0, 500), p.Output(clock).Force);
        p.PowerSupplyRatio = 0.2;
        Assert.Equal(Vector3.Zero, p.Output(clock).Force);
    }

    [Fact]
    public void Corrupted_packets_are_ignored()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 1, 0, 0, 0).Corrupted(clock.Rng), clock);
        Assert.Equal(Vector3.Zero, p.Output(clock).Force);
    }

    [Fact]
    public void Packets_for_someone_else_or_other_topics_are_ignored()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Packet.Create(new Address(1), new Address(99), PacketKind.Control, ControlTopic.Setpoint, new[] { 1.0, 0, 0, 0 }, 1), clock);
        Feed(p, Packet.Create(new Address(1), new Address(30), PacketKind.Control, ControlTopic.Throttle, new[] { 1.0, 0, 0, 0 }, 2), clock);
        Assert.Equal(Vector3.Zero, p.Output(clock).Force);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_setpoints_are_ignored(double bad)
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 0.4, 0, 0, 0), clock);
        Feed(p, Setpoint(2, bad, 0, 0, 0), clock);
        Assert.Equal(new Vector3(0, 0, 400), p.Output(clock).Force);
    }

    [Fact]
    public void Wrong_length_setpoints_are_ignored()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 1, 0), clock);
        Assert.Equal(Vector3.Zero, p.Output(clock).Force);
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(1, 9, 9, -9, 0), clock);
        var (force, torque) = p.Output(clock);
        Assert.Equal(new Vector3(0, 0, 1000), force);
        Assert.Equal(new Vector3(100, 0, -300), torque);
    }

    [Fact]
    public void Older_sequence_numbers_are_ignored()
    {
        var clock = new SimClock(1);
        var p = Make();
        Feed(p, Setpoint(5, 0.5, 0, 0, 0), clock);
        Feed(p, Setpoint(4, 1.0, 0, 0, 0), clock);
        Assert.Equal(new Vector3(0, 0, 500), p.Output(clock).Force);
    }

    [Fact]
    public void Power_demand_rises_with_throttle()
    {
        var clock = new SimClock(1);
        var p = Make();
        p.Process(clock);
        Assert.Equal(20, p.PowerDemand);
        Feed(p, Setpoint(1, 1, 0, 0, 0), clock);
        Assert.Equal(220, p.PowerDemand);
    }

    [Fact]
    public void Setpoints_are_accepted_from_any_interface()
    {
        var clock = new SimClock(1);
        var p = Make();
        var second = p.AddInterface("backup");
        second.Inbox.Enqueue(Setpoint(1, 1, 0, 0, 0));
        p.Process(clock);
        Assert.Equal(new Vector3(0, 0, 1000), p.Output(clock).Force);
    }
}
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `dotnet test --filter "FullyQualifiedName~PropulsionTests"`
Expected: FAIL (compilation).

- [ ] **Step 3: Implémenter**

`src/Starship.Sim/PropulsionSystem.cs`:

```csharp
using System.Numerics;

namespace Starship.Sim;

public sealed class PropulsionSystem : Device
{
    private const double BaseDemand = 20;
    private const double DemandPerThrottle = 200;

    private double _throttle;
    private Vector3 _stick;                       // x pitch, y yaw, z roll, each in [-1, 1]
    private long _setpointTick = long.MinValue / 2;
    private int _lastSeq;

    public PropulsionSystem(Address address, Address computer, double maxThrust, Vector3 maxTorque)
        : base(address, "Propulsion", DeviceKind.Propulsion, computer)
    {
        MaxThrust = maxThrust;
        MaxTorque = maxTorque;
        In = AddInterface("in");
        PowerDemand = BaseDemand;
    }

    public NetInterface In { get; }
    public double MaxThrust { get; }
    public Vector3 MaxTorque { get; }

    public override void Process(SimClock clock)
    {
        foreach (var iface in Interfaces)
            while (iface.Inbox.TryDequeue(out var packet)) Handle(packet, clock);
        PowerDemand = BaseDemand + DemandPerThrottle * (HasFreshSetpoint(clock) ? _throttle : 0.0);
    }

    public (Vector3 Force, Vector3 Torque) Output(SimClock clock)
    {
        if (!Powered || !HasFreshSetpoint(clock)) return (Vector3.Zero, Vector3.Zero);
        var ratio = (float)PowerSupplyRatio;
        var force = new Vector3(0, 0, (float)(_throttle * MaxThrust)) * ratio;
        var torque = _stick * MaxTorque * ratio;
        return (force, torque);
    }

    public override TelemetryReport Report()
    {
        var throttle = _throttle;
        return new TelemetryReport(Kind, PowerDemand * PowerSupplyRatio, 20 + 80 * throttle, throttle, Health);
    }

    private bool HasFreshSetpoint(SimClock clock) =>
        clock.Tick - _setpointTick <= SimConstants.SetpointHoldTicks;

    private void Handle(Packet packet, SimClock clock)
    {
        if (!packet.IsValid || packet.Dst != Address) return;
        if (packet.Kind != PacketKind.Control || packet.Topic != ControlTopic.Setpoint) return;
        if (packet.Seq <= _lastSeq) return;
        var p = packet.Payload;
        if (p.Length != 4 || !p.All(double.IsFinite)) return;

        _lastSeq = packet.Seq;
        _throttle = Math.Clamp(p[0], 0, 1);
        _stick = new Vector3(
            (float)Math.Clamp(p[1], -1, 1),    // pitch -> x
            (float)Math.Clamp(p[3], -1, 1),    // yaw   -> y
            (float)Math.Clamp(p[2], -1, 1));   // roll  -> z
        _setpointTick = clock.Tick;
    }
}
```

- [ ] **Step 4: Lancer, vérifier le succès**

Run: `dotnet test --filter "FullyQualifiedName~PropulsionTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: PropulsionSystem with setpoint hold, power scaling, validation"
```

---

### Task 11: Ship.Step, ShipBuilder et scénarios de bout en bout

**Files:**
- Modify: `src/Starship.Sim/Ship.cs` (ajout de `Step()`)
- Create: `src/Starship.Sim/ShipBuilder.cs`
- Test: `tests/Starship.Sim.Tests/ShipScenarioTests.cs`

**Interfaces:**
- Consumes: tout ce qui précède.
- Produces: `void Ship.Step()` (ordre: émission des appareils alimentés, `Cable.Step`, `Router.Step`, `Process`, répartition d'énergie, sortie propulsion → `Body.Step`, `Clock.Advance()`; les appareils non alimentés vident leur boîte d'entrée, un ordinateur non alimenté vide son routeur). `record DefaultShip(Ship Ship, ThrottleLever Throttle, Joystick Joystick, BoardComputer Computer, PowerPlant Plant, PropulsionSystem Propulsion, IReadOnlyDictionary<string, Cable> Cables)`. `static DefaultShip ShipBuilder.CreateDefault(int seed, RouterModel model, double cableQuality = 1.0)`. Adresses: ordinateur 1, manette 10, joystick 11, groupe 20, propulsion 30. Câbles nommés `throttle`, `joystick`, `plant`, `propulsion` (longueurs 4, 4, 3, 5 m). Corps: 10 000 kg, inertie (50 000, 50 000, 20 000), poussée max 200 000 N (20 m/s²), couple max 100 000 N·m par axe, groupe 1 000 W.

- [ ] **Step 1: Écrire les tests de scénario**

`tests/Starship.Sim.Tests/ShipScenarioTests.cs`:

```csharp
using System.Numerics;
using Starship.Sim;
using Xunit;

namespace Starship.Sim.Tests;

public class ShipScenarioTests
{
    private static void Run(DefaultShip s, int ticks)
    {
        for (int i = 0; i < ticks; i++) s.Ship.Step();
    }

    [Fact]
    public void Full_throttle_accelerates_the_ship_forward()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic);
        s.Throttle.Set(1.0);
        Run(s, 600);
        Assert.InRange(s.Ship.Body.Velocity.Z, 100f, 200f);
        Assert.InRange(Math.Abs(s.Ship.Body.Velocity.X), 0f, 1e-3f);
        Assert.True(s.Ship.Body.Position.Z > 0);
    }

    [Fact]
    public void Telemetry_of_every_device_reaches_the_computer()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic);
        Run(s, 120);
        foreach (var address in new[] { 20, 30 })
        {
            Assert.True(s.Computer.Status.ContainsKey(new Address(address)));
            Assert.False(s.Computer.IsStale(new Address(address), s.Ship.Clock));
        }
    }

    [Fact]
    public void Without_throttle_the_ship_stays_put()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic);
        Run(s, 300);
        Assert.Equal(Vector3.Zero, s.Ship.Body.Velocity);
    }

    [Fact]
    public void Stick_input_rotates_the_ship()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic);
        s.Joystick.Set(1, 0, 0);
        Run(s, 120);
        Assert.True(s.Ship.Body.AngularVelocity.X > 0.5f);
        Assert.InRange(s.Ship.Body.Velocity.Length(), 0f, 1e-3f);
    }

    [Fact]
    public void Same_seed_gives_identical_runs_with_noisy_cables()
    {
        (Vector3 pos, Vector3 vel, Quaternion rot, int dropped) Simulate(int seed)
        {
            var s = ShipBuilder.CreateDefault(seed, RouterModels.Basic, cableQuality: 0.8);
            s.Throttle.Set(0.8);
            s.Joystick.Set(0.3, 0, 0.2);
            Run(s, 600);
            return (s.Ship.Body.Position, s.Ship.Body.Velocity, s.Ship.Body.Orientation, s.Computer.Router.DroppedCount);
        }

        var first = Simulate(7);
        var second = Simulate(7);
        Assert.Equal(first, second);
        Assert.True(first.dropped > 0, "noisy cables should corrupt some packets");
    }

    [Fact]
    public void Burnt_throttle_cable_without_redundancy_cuts_the_thrust_but_not_the_momentum()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic);
        s.Throttle.Set(1.0);
        Run(s, 120);
        s.Cables["throttle"].Burn();
        Run(s, 100);                                   // control timeout + setpoint hold elapsed
        var v1 = s.Ship.Body.Velocity;
        var p1 = s.Ship.Body.Position;
        Run(s, 60);
        Assert.True(v1.Z > 10f);
        Assert.Equal(v1, s.Ship.Body.Velocity);        // no more thrust
        Assert.True(s.Ship.Body.Position.Z > p1.Z);    // still drifting
    }

    [Fact]
    public void Replacing_the_burnt_cable_restores_control()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic);
        s.Throttle.Set(1.0);
        Run(s, 60);
        s.Cables["throttle"].Burn();
        Run(s, 100);
        var v1 = s.Ship.Body.Velocity.Z;
        s.Ship.Replace(s.Cables["throttle"], new Cable("throttle2", 1.0, 4));
        Run(s, 120);
        Assert.True(s.Ship.Body.Velocity.Z > v1 + 10f);
    }

    [Fact]
    public void Redundant_propulsion_cable_keeps_the_thrust_after_a_failure()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Intermediate);
        var port = s.Computer.Router.FindFreePort(DeviceKind.Propulsion)!;
        s.Ship.Connect(port, s.Propulsion.AddInterface("backup"), new Cable("backup", 1.0, 3));

        s.Throttle.Set(1.0);
        Run(s, 120);
        s.Cables["propulsion"].Burn();
        Run(s, 200);                                   // failover detection + settling
        var v1 = s.Ship.Body.Velocity.Z;
        Run(s, 100);
        Assert.True(s.Ship.Body.Velocity.Z - v1 > 25f, "thrust should continue through the backup route");
    }

    [Fact]
    public void Without_a_backup_cable_a_propulsion_cable_failure_cuts_the_thrust()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Intermediate);
        s.Throttle.Set(1.0);
        Run(s, 120);
        s.Cables["propulsion"].Burn();
        Run(s, 200);
        var v1 = s.Ship.Body.Velocity;
        Run(s, 100);
        Assert.Equal(v1, s.Ship.Body.Velocity);
    }

    [Fact]
    public void Power_outage_stops_the_thrust_and_recovery_restores_it_without_stale_replay()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Basic);
        s.Throttle.Set(1.0);
        Run(s, 120);
        s.Plant.Online = false;
        Run(s, 60);
        var v1 = s.Ship.Body.Velocity;
        Run(s, 60);
        Assert.Equal(v1, s.Ship.Body.Velocity);
        Assert.Empty(s.Propulsion.In.Inbox);
        Assert.All(s.Computer.Router.Ports, p => Assert.Empty(p.Inbox));

        s.Plant.Online = true;
        Run(s, 120);
        Assert.True(s.Ship.Body.Velocity.Z > v1.Z + 10f);
    }

    [Fact]
    public void Default_ship_is_fully_wired()
    {
        var s = ShipBuilder.CreateDefault(1, RouterModels.Advanced);
        Assert.Equal(4, s.Cables.Count);
        Assert.Equal(5, s.Ship.Devices.Count);
        Assert.All(s.Ship.Cables, c => Assert.NotNull(c.A));
    }
}
```

- [ ] **Step 2: Ajouter le test de brownout**

Dans `ShipScenarioTests.cs`, ajouter dans la classe (avant l'accolade fermante):

```csharp
    [Fact]
    public void Brownout_reduces_thrust_but_the_ship_keeps_flying()
    {
        var full = ShipBuilder.CreateDefault(1, RouterModels.Basic, plantCapacity: 1000);
        var weak = ShipBuilder.CreateDefault(1, RouterModels.Basic, plantCapacity: 150);   // demand ~260 W -> ratio ~0.58
        full.Throttle.Set(1.0);
        weak.Throttle.Set(1.0);
        Run(full, 600);
        Run(weak, 600);
        Assert.True(weak.Propulsion.PowerSupplyRatio < 1.0);
        Assert.True(weak.Propulsion.Powered);
        Assert.InRange(weak.Ship.Body.Velocity.Z, 20f, full.Ship.Body.Velocity.Z - 20f);
    }
```

et ajouter le paramètre `double plantCapacity = 1_000` à la signature de `CreateDefault` (voir Step 4).

- [ ] **Step 3: Lancer, vérifier l'échec**

Run: `dotnet test --filter "FullyQualifiedName~ShipScenarioTests"`
Expected: FAIL (compilation: `ShipBuilder`, `DefaultShip`, `Ship.Step` introuvables).

- [ ] **Step 4: Implémenter `Step()` et `ShipBuilder`**

Dans `src/Starship.Sim/Ship.cs`, ajouter la méthode suivante dans la classe `Ship`:

```csharp
    /// <summary>Advance the whole ship by one fixed tick.</summary>
    public void Step()
    {
        foreach (var device in _devices)
        {
            if (device.Powered) device.Emit(Clock);
            else device.DiscardInbound();
        }

        foreach (var cable in _cables) cable.Step(Clock);

        var computer = _devices.OfType<BoardComputer>().FirstOrDefault();
        if (computer != null)
        {
            if (computer.Powered) computer.Router.Step(Clock);
            else computer.Router.DiscardTraffic();
        }

        foreach (var device in _devices)
            if (device.Powered) device.Process(Clock);

        PowerDistribution.Apply(_devices.OfType<PowerPlant>().FirstOrDefault(), _devices);

        var force = System.Numerics.Vector3.Zero;
        var torque = System.Numerics.Vector3.Zero;
        foreach (var propulsion in _devices.OfType<PropulsionSystem>())
        {
            var (f, t) = propulsion.Output(Clock);
            force += f;
            torque += t;
        }
        Body.Step(force, torque, SimClock.Dt);

        Clock.Advance();
    }
```

`src/Starship.Sim/ShipBuilder.cs`:

```csharp
using System.Numerics;

namespace Starship.Sim;

public sealed record DefaultShip(
    Ship Ship,
    ThrottleLever Throttle,
    Joystick Joystick,
    BoardComputer Computer,
    PowerPlant Plant,
    PropulsionSystem Propulsion,
    IReadOnlyDictionary<string, Cable> Cables);

public static class ShipBuilder
{
    /// <summary>
    /// Cockpit, computer with the given router model, power plant and thruster, all wired to the computer.
    /// Cable noise also depends on length, so even quality 1.0 is not perfectly lossless.
    /// </summary>
    public static DefaultShip CreateDefault(int seed, RouterModel model, double cableQuality = 1.0, double plantCapacity = 1_000)
    {
        var ship = new Ship(seed, new ShipBody(10_000, new Vector3(50_000, 50_000, 20_000)));
        var computerAddress = new Address(1);
        var computer = ship.Add(new BoardComputer(computerAddress, model));
        var throttle = ship.Add(new ThrottleLever(new Address(10), computerAddress));
        var joystick = ship.Add(new Joystick(new Address(11), computerAddress));
        var plant = ship.Add(new PowerPlant(new Address(20), computerAddress, plantCapacity));
        var propulsion = ship.Add(new PropulsionSystem(
            new Address(30), computerAddress, 200_000, new Vector3(100_000, 100_000, 100_000)));

        var cables = new Dictionary<string, Cable>();
        void Wire(string name, NetInterface iface, DeviceKind kind, double length)
        {
            var port = computer.Router.FindFreePort(kind)
                ?? throw new InvalidOperationException($"No free router port for {kind}");
            cables[name] = ship.Connect(port, iface, new Cable(name, cableQuality, length));
        }

        Wire("throttle", throttle.Out, DeviceKind.Throttle, 4);
        Wire("joystick", joystick.Out, DeviceKind.Joystick, 4);
        Wire("plant", plant.Out, DeviceKind.PowerPlant, 3);
        Wire("propulsion", propulsion.In, DeviceKind.Propulsion, 5);

        return new DefaultShip(ship, throttle, joystick, computer, plant, propulsion, cables);
    }
}
```

- [ ] **Step 5: Lancer toute la suite**

Run: `dotnet test`
Expected: PASS (tous les tests). Si un scénario échoue, ne pas assouplir l'assertion: diagnostiquer avec `superpowers:systematic-debugging` (candidats: ordre des étapes de `Step`, `LastRxTick`, horloges de timeout).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: Ship.Step, default ship builder, end-to-end scenarios"
```

---

### Task 12: Interface console

**Files:**
- Modify: `src/Starship.Cli/Program.cs`

**Interfaces:**
- Consumes: `ShipBuilder.CreateDefault`, `DefaultShip`, `Ship.Step`, `Ship.Replace`, `Cable.Burn`, `BoardComputer.Status/AgeOf/IsStale`, `Router.DroppedCount`.
- Produces: exécutable `dotnet run --project src/Starship.Cli -- [--router basic|intermediate|advanced] [--headless <ticks>]`. Mode interactif: `W/S` poussée ±5 %, `↑/↓` tangage ±0,25, `←/→` lacet ±0,25, `A/D` roulis ±0,25, `Espace` manche neutre, `X` brûle le câble de la manette, `R` remplace tous les câbles brûlés par des neufs, `Q` quitte. Affiche position, vitesse, cap, télémétrie avec âge et `STALE`, santé des câbles, paquets perdus du routeur.

- [ ] **Step 1: Écrire le programme**

Remplacer `src/Starship.Cli/Program.cs` par:

```csharp
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
            case ConsoleKey.X: ship.Cables["throttle"].Burn(); break;
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
    foreach (var (key, cable) in ship.Cables.Where(kv => kv.Value.IsBurnt).ToList())
    {
        var fresh = new Cable(cable.Name + "'", 0.9, cable.Length);
        ship.Ship.Replace(cable, fresh);
        ship.Cables[key] = fresh;
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
```

- [ ] **Step 2: Vérifier la compilation et le mode sans interface**

Run: `dotnet build && dotnet run --project src/Starship.Cli -- --headless 600`
Expected: build sans erreur; affichage d'un état avec vitesse sur Z d'environ 40 à 60 m/s, télémétrie de `PowerPlant` et `Propulsion` avec `ok`, câbles listés avec santé 100 %.

- [ ] **Step 3: Vérifier chaque gamme de routeur**

Run: `dotnet run --project src/Starship.Cli -- --router intermediate --headless 300` puis `dotnet run --project src/Starship.Cli -- --router advanced --headless 300`
Expected: même type d'état, la première ligne affiche `Router: Intermediate`, puis `Router: Advanced`.

- [ ] **Step 4: Vérification manuelle interactive**

Run (dans un vrai terminal): `dotnet run --project src/Starship.Cli`
Expected: `W` fait monter la poussée et la vitesse; `X` brûle le câble de la manette; après environ 1 à 2 s la vitesse cesse d'augmenter (la dernière donnée de la manette est périmée) et la télémétrie de la manette passe en `STALE`; `R` remplace le câble et la commande reprend; `Q` quitte proprement.

- [ ] **Step 5: Lancer toute la suite et commit**

Run: `dotnet test`
Expected: PASS.

```bash
git add -A
git commit -m "feat: console interface with headless mode"
```

---

## Self-review

**Couverture de la spec**
- Objectif / critères de succès: vaisseau complet en console (Task 11, 12), télémétrie avec obsolescence (Task 9, 12), câblage à chaud (Task 6, 11, 12), câble brûlé + failover (Task 4, 11), déterminisme (Task 11), tests (toutes).
- 3 Architecture: `SimClock` (1), `Ship` (6, 11), `ShipBody` (5), `Device`/appareils (3, 7, 8, 9, 10), `Router` (4), `Cable` (3), `Packet` (2).
- 4.1 paquets (2, avec `Topic`, aligné en Task 1), 4.2 câbles (3), 4.3 topologie éditable avec grâce de route (6), 4.4 gammes (4).
- 5 flux d'un tick: `Ship.Step` (11), ordre identique à la spec; l'étape "surveillance" (heartbeats) est assurée par `Router.IsUp` calculé à la demande, la chauffe de câble reste hors périmètre (spec section 9).
- 6 télémétrie (3, 9), 7 pannes: câble brûlé, failover, perte de commande (9 + 10), télémétrie perdue (9), panne d'énergie (3, 8, 11).
- 8 tests: déterminisme, une par panne, bruit vs longueur/qualité (3), physique (5), gammes de routeur (4). Brownout: couvert en Task 8 (répartition) et Task 11 (`Brownout_reduces_thrust…`).

**Placeholders:** aucun. Le test de brownout provisoire de Task 11 est explicitement remplacé en Step 2 de la même tâche.

**Cohérence des types:** `Router.OnConnected(RouterPort, long)`, `Cable.Peer`, `ShipBody.Step(Vector3, Vector3, double)`, `PropulsionSystem.Output(SimClock)`, `BoardComputer.AgeOf/IsStale`, `ShipBuilder.CreateDefault(seed, model, cableQuality, plantCapacity)` sont utilisés avec les mêmes signatures partout. `TestWiring.LinkPort` (Task 4) est utilisé en Task 9.

**Review Focus:** lignes 1 à 5 testées (Tasks 9/10 NaN et longueurs, Task 4 destination inconnue, Task 11 reprise d'énergie, Task 7 appareil sans câble, Tasks 3/6 câble endommagé ou remplacé).
