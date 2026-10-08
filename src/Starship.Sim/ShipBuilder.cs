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
