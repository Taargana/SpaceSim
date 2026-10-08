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
