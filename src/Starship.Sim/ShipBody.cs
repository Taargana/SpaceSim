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
