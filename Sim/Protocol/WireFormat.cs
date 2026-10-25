using System.Numerics;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Protocol;

/// <summary>One player in a snapshot.</summary>
public readonly record struct PlayerSnapshot(int PeerId, PlayerState State, float Yaw, float Pitch);

/// <summary>
/// The server's view of every player at one tick. <see cref="AckSeq"/> is the last input the server has
/// processed from the client receiving this snapshot.
/// </summary>
public sealed record Snapshot(uint Tick, uint AckSeq, IReadOnlyList<PlayerSnapshot> Players);

/// <summary>
/// Byte layouts for messages sent often or in bulk. Little-endian. Decoders treat the bytes as untrusted:
/// bad data throws <see cref="InvalidDataException"/>, and input values are clamped to legal ranges.
/// </summary>
public static class WireFormat
{
    public const int MaxInputsPerPacket = 16;
    public const int MaxPlayersPerSnapshot = 255;

    private const InputButtons KnownButtons = InputButtons.Jump | InputButtons.Sprint | InputButtons.Descend | InputButtons.Fly;

    public static byte[] EncodeInputs(IReadOnlyList<PlayerInput> inputs)
    {
        if (inputs.Count > MaxInputsPerPacket)
        {
            throw new ArgumentException($"At most {MaxInputsPerPacket} inputs per packet.", nameof(inputs));
        }

        return Encode(writer =>
        {
            writer.Write((byte)inputs.Count);
            foreach (PlayerInput input in inputs)
            {
                writer.Write(input.Seq);
                writer.Write(input.MoveX);
                writer.Write(input.MoveZ);
                writer.Write(input.Yaw);
                writer.Write(input.Pitch);
                writer.Write((byte)input.Buttons);
            }
        });
    }

    public static IReadOnlyList<PlayerInput> DecodeInputs(byte[] data) => Decode(data, reader =>
    {
        int count = reader.ReadByte();
        if (count > MaxInputsPerPacket)
        {
            throw new InvalidDataException($"Input packet claims {count} inputs.");
        }

        var inputs = new PlayerInput[count];
        for (int i = 0; i < count; i++)
        {
            inputs[i] = new PlayerInput(
                reader.ReadUInt32(),
                Clamp(reader.ReadSingle(), -1, 1),
                Clamp(reader.ReadSingle(), -1, 1),
                Finite(reader.ReadSingle()),
                Clamp(reader.ReadSingle(), -MathF.PI / 2, MathF.PI / 2),
                (InputButtons)reader.ReadByte() & KnownButtons);
        }

        return inputs;
    });

    public static byte[] EncodeSnapshot(Snapshot snapshot)
    {
        if (snapshot.Players.Count > MaxPlayersPerSnapshot)
        {
            throw new ArgumentException($"At most {MaxPlayersPerSnapshot} players per snapshot.", nameof(snapshot));
        }

        return Encode(writer =>
        {
            writer.Write(snapshot.Tick);
            writer.Write(snapshot.AckSeq);
            writer.Write((byte)snapshot.Players.Count);
            foreach (PlayerSnapshot player in snapshot.Players)
            {
                writer.Write(player.PeerId);
                Write(writer, player.State.Position);
                Write(writer, player.State.Velocity);
                writer.Write(player.Yaw);
                writer.Write(player.Pitch);
                writer.Write((byte)((player.State.OnGround ? 1 : 0) | (player.State.Flying ? 2 : 0)));
            }
        });
    }

    public static Snapshot DecodeSnapshot(byte[] data) => Decode(data, reader =>
    {
        uint tick = reader.ReadUInt32();
        uint ack = reader.ReadUInt32();
        int count = reader.ReadByte();
        var players = new PlayerSnapshot[count];
        for (int i = 0; i < count; i++)
        {
            int peer = reader.ReadInt32();
            Vector3 position = Read(reader);
            Vector3 velocity = Read(reader);
            float yaw = reader.ReadSingle();
            float pitch = reader.ReadSingle();
            byte flags = reader.ReadByte();
            players[i] = new PlayerSnapshot(peer, new PlayerState(position, velocity, (flags & 1) != 0, (flags & 2) != 0), yaw, pitch);
        }

        return new Snapshot(tick, ack, players);
    });

    /// <summary>Block edits, 8 bytes each. Positions must fit in 0 to 65535, which every map does.</summary>
    public static byte[] EncodeEdits(IReadOnlyCollection<VoxelEdit> edits) => Encode(writer =>
    {
        writer.Write(edits.Count);
        foreach (VoxelEdit edit in edits)
        {
            writer.Write(checked((ushort)edit.Position.X));
            writer.Write(checked((ushort)edit.Position.Y));
            writer.Write(checked((ushort)edit.Position.Z));
            writer.Write((ushort)edit.Block);
        }
    });

    public static IReadOnlyList<VoxelEdit> DecodeEdits(byte[] data) => Decode(data, reader =>
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > (data.Length - 4) / 8)
        {
            throw new InvalidDataException($"Edit list claims {count} edits in {data.Length} bytes.");
        }

        var edits = new VoxelEdit[count];
        for (int i = 0; i < count; i++)
        {
            var position = new BlockPos(reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16());
            edits[i] = new VoxelEdit(position, (Block)reader.ReadUInt16());
        }

        return edits;
    });

    private static byte[] Encode(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            write(writer);
        }

        return stream.ToArray();
    }

    private static T Decode<T>(byte[] data, Func<BinaryReader, T> read)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(data));
            T result = read(reader);
            if (reader.BaseStream.Position != data.Length)
            {
                throw new InvalidDataException("Trailing bytes after the message.");
            }

            return result;
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("Message is truncated.", e);
        }
    }

    private static void Write(BinaryWriter writer, Vector3 v)
    {
        writer.Write(v.X);
        writer.Write(v.Y);
        writer.Write(v.Z);
    }

    private static Vector3 Read(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;

    private static float Clamp(float value, float min, float max) => Math.Clamp(Finite(value), min, max);
}
