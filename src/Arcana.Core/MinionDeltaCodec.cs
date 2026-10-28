namespace Arcana.Core;

/// <summary>Campos exactos de cada enemigo respecto a una base inmutable. No cuantiza el combate.</summary>
public sealed class MinionDeltaCodec(MinionSnapshot[] baseline)
{
    private const ushort All = ushort.MaxValue;
    private readonly Dictionary<int, MinionSnapshot> _baseline = baseline.ToDictionary(m => m.Id);
    public byte[] Encode(MinionSnapshot[] current)
    {
        using var stream = new MemoryStream(current.Length * 24 + 2);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)current.Length);
        foreach (var m in current)
        {
            ushort mask = All;
            if (_baseline.TryGetValue(m.Id, out var old))
            {
                mask = 0;
                if (m.Position != old.Position) mask |= 1;
                if (m.Facing != old.Facing) mask |= 2;
                if (m.Health != old.Health) mask |= 4;
                if (m.MaxHealth != old.MaxHealth) mask |= 8;
                if (m.Wet != old.Wet) mask |= 16;
                if (m.Burning != old.Burning) mask |= 32;
                if (m.Chill != old.Chill) mask |= 64;
                if (m.Frozen != old.Frozen) mask |= 128;
                if (m.Casting != old.Casting) mask |= 256;
                if (!m.Elements.AsSpan().SequenceEqual(old.Elements)) mask |= 512;
                if (m.Empowered != old.Empowered) mask |= 1024;
                if (m.SpeedBoostSeconds != old.SpeedBoostSeconds) mask |= 2048;
                if (m.AttackWarningSeconds != old.AttackWarningSeconds) mask |= 4096;
                if (m.Kind != old.Kind) mask |= 8192;
                if (m.Shield != old.Shield || m.ShieldMax != old.ShieldMax || m.VulnerableElement != old.VulnerableElement || m.ArcaneMarked != old.ArcaneMarked) mask |= 16384;
                if (m.FocusTargetId != old.FocusTargetId) mask |= 32768;
            }
            writer.Write(m.Id); writer.Write(mask);
            if ((mask & 1) != 0) { writer.Write(m.Position.X); writer.Write(m.Position.Y); }
            if ((mask & 2) != 0) { writer.Write(m.Facing.X); writer.Write(m.Facing.Y); }
            if ((mask & 4) != 0) writer.Write(m.Health);
            if ((mask & 8) != 0) writer.Write(m.MaxHealth);
            if ((mask & 16) != 0) writer.Write(m.Wet);
            if ((mask & 32) != 0) writer.Write(m.Burning);
            if ((mask & 64) != 0) writer.Write(m.Chill);
            if ((mask & 128) != 0) writer.Write(m.Frozen);
            if ((mask & 256) != 0) writer.Write(m.Casting);
            if ((mask & 512) != 0) { writer.Write((byte)m.Elements.Length); foreach (var e in m.Elements) writer.Write((byte)e); }
            if ((mask & 1024) != 0) writer.Write(m.Empowered);
            if ((mask & 2048) != 0) writer.Write(m.SpeedBoostSeconds);
            if ((mask & 4096) != 0) writer.Write(m.AttackWarningSeconds);
            if ((mask & 8192) != 0) writer.Write((byte)m.Kind);
            if ((mask & 16384) != 0) { writer.Write(m.Shield); writer.Write(m.ShieldMax); writer.Write((sbyte)(m.VulnerableElement.HasValue ? (int)m.VulnerableElement.Value : -1)); writer.Write(m.ArcaneMarked); }
            if ((mask & 32768) != 0) writer.Write(m.FocusTargetId);
        }
        return stream.ToArray();
    }

    public MinionSnapshot[] Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream);
            int count = reader.ReadUInt16();
            if (count > DefenseRules.MaxMinions) throw new InvalidDataException("Demasiados enemigos.");
            var result = new MinionSnapshot[count]; var ids = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt32(); ushort mask = reader.ReadUInt16();
                if (id < 1000 || !ids.Add(id) || (mask & ~All) != 0) throw new InvalidDataException("Enemigo inválido.");
                if (!_baseline.TryGetValue(id, out var old) && mask != All) throw new InvalidDataException("Falta la base del enemigo.");
                old ??= new(id, default, default, default, 0, 0, 0, 0, 0, 0, false, [], false);
                var position = (mask & 1) != 0 ? new V2(reader.ReadSingle(), reader.ReadSingle()) : old.Position;
                var facing = (mask & 2) != 0 ? new V2(reader.ReadSingle(), reader.ReadSingle()) : old.Facing;
                float health = (mask & 4) != 0 ? reader.ReadSingle() : old.Health;
                float maximum = (mask & 8) != 0 ? reader.ReadSingle() : old.MaxHealth;
                float wet = (mask & 16) != 0 ? reader.ReadSingle() : old.Wet;
                float burning = (mask & 32) != 0 ? reader.ReadSingle() : old.Burning;
                float chill = (mask & 64) != 0 ? reader.ReadSingle() : old.Chill;
                float frozen = (mask & 128) != 0 ? reader.ReadSingle() : old.Frozen;
                bool casting = (mask & 256) != 0 ? reader.ReadBoolean() : old.Casting;
                var elements = old.Elements;
                if ((mask & 512) != 0)
                {
                    int length = reader.ReadByte(); if (length > 3) throw new InvalidDataException("Demasiados elementos.");
                    elements = new Element[length]; for (int e = 0; e < length; e++) elements[e] = (Element)reader.ReadByte();
                }
                bool empowered = (mask & 1024) != 0 ? reader.ReadBoolean() : old.Empowered;
                float speed = (mask & 2048) != 0 ? reader.ReadSingle() : old.SpeedBoostSeconds;
                float warning = (mask & 4096) != 0 ? reader.ReadSingle() : old.AttackWarningSeconds;
                var kind = (mask & 8192) != 0 ? (MinionKind)reader.ReadByte() : old.Kind;
                float shield = old.Shield, shieldMax = old.ShieldMax; Element? weakness = old.VulnerableElement;
                bool marked = old.ArcaneMarked;
                if ((mask & 16384) != 0)
                {
                    shield = reader.ReadSingle(); shieldMax = reader.ReadSingle(); int element = reader.ReadSByte();
                    if (element != -1 && (element < 0 || !DefenseRules.ValidPrismElement((Element)element))) throw new InvalidDataException("Punto débil inválido.");
                    weakness = element == -1 ? null : (Element)element;
                    marked = reader.ReadBoolean();
                }
                int focus = (mask & 32768) != 0 ? reader.ReadInt32() : old.FocusTargetId;
                result[i] = new(id, kind, position, facing, health, maximum, wet, burning, chill, frozen, casting, elements, empowered)
                    { SpeedBoostSeconds = speed, AttackWarningSeconds = warning, Shield = shield, ShieldMax = shieldMax,
                        VulnerableElement = weakness, FocusTargetId = focus, ArcaneMarked = marked };
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Datos de enemigos adicionales.");
            return result;
        }
        catch (EndOfStreamException e) { throw new InvalidDataException("Estado de enemigos truncado.", e); }
    }
}
