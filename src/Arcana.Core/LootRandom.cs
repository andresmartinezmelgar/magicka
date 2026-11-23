using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Arcana.Core;

public sealed class LootRandom : Random
{
    public override int Next(int maxValue) => RandomNumberGenerator.GetInt32(maxValue);
    public override int Next(int minValue, int maxValue) => RandomNumberGenerator.GetInt32(minValue, maxValue);
    public override double NextDouble()
    {
        Span<byte> bytes = stackalloc byte[8]; RandomNumberGenerator.Fill(bytes);
        return (BitConverter.ToUInt64(bytes) >> 11) / 9007199254740992d;
    }
}
