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
