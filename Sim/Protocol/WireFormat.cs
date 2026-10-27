using System.Numerics;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Players;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Protocol;

/// <summary>One player in a snapshot. <see cref="PlaneId"/> is the plane they're flying, or 0.</summary>
public readonly record struct PlayerSnapshot(int PeerId, PlayerState State, float Yaw, float Pitch, int PlaneId = 0);

/// <summary>One plane in a snapshot. <see cref="PilotPeerId"/> is 0 when nobody is flying it.</summary>
public readonly record struct PlaneSnapshot(int Id, PlaneState State, int PilotPeerId, Team Team = Team.None);

/// <summary>
/// The server's view of the match, every player and every plane at one tick. <see cref="AckSeq"/> is the
/// last input the server has processed from the client receiving this snapshot.
/// </summary>
/// <param name="NukeReadyIn">
/// Seconds until the receiving client's team can drop its nuke: 0 when ready, negative when nukes aren't
/// available at all (outside phase 3).
/// </param>
public sealed record Snapshot(uint Tick, uint AckSeq, IReadOnlyList<PlayerSnapshot> Players, IReadOnlyList<PlaneSnapshot> Planes, MatchStatus Match = default, float NukeReadyIn = -1);

/// <summary>
/// Byte layouts for messages sent often or in bulk. Little-endian. Decoders treat the bytes as untrusted:
/// bad data throws <see cref="InvalidDataException"/>, and input values are clamped to legal ranges.
/// </summary>
public static class WireFormat
{
    public const int MaxInputsPerPacket = 16;
    public const int MaxPlayersPerSnapshot = 255;
    public const int MaxPlanesPerSnapshot = 255;

    private const InputButtons KnownButtons = InputButtons.Jump | InputButtons.Sprint | InputButtons.Descend | InputButtons.Fly | InputButtons.Use | InputButtons.Fire | InputButtons.FireNuke;

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
        if (snapshot.Players.Count > MaxPlayersPerSnapshot || snapshot.Planes.Count > MaxPlanesPerSnapshot)
        {
            throw new ArgumentException($"At most {MaxPlayersPerSnapshot} players and {MaxPlanesPerSnapshot} planes per snapshot.", nameof(snapshot));
        }

        return Encode(writer =>
        {
            writer.Write(snapshot.Tick);
            writer.Write(snapshot.AckSeq);
            writer.Write((byte)snapshot.Match.Stage);
            writer.Write((byte)snapshot.Match.Phase);
            writer.Write(snapshot.Match.StageSeconds);
            writer.Write((byte)snapshot.Match.Winner);
            writer.Write((byte)snapshot.Match.Reason);
            writer.Write(snapshot.NukeReadyIn);
            writer.Write((byte)snapshot.Players.Count);
            foreach (PlayerSnapshot player in snapshot.Players)
            {
                writer.Write(player.PeerId);
                Write(writer, player.State.Position);
                Write(writer, player.State.Velocity);
                writer.Write(player.Yaw);
                writer.Write(player.Pitch);
                writer.Write((byte)((player.State.OnGround ? 1 : 0) | (player.State.Flying ? 2 : 0)));
                writer.Write(player.PlaneId);
            }

            writer.Write((byte)snapshot.Planes.Count);
            foreach (PlaneSnapshot plane in snapshot.Planes)
            {
                writer.Write(plane.Id);
                writer.Write(plane.PilotPeerId);
                writer.Write((byte)plane.Team);
                Write(writer, plane.State.Position);
                writer.Write(plane.State.Rotation.X);
                writer.Write(plane.State.Rotation.Y);
                writer.Write(plane.State.Rotation.Z);
                writer.Write(plane.State.Rotation.W);
                writer.Write(plane.State.Airspeed);
                writer.Write(plane.State.Sink);
                writer.Write(plane.State.Throttle);
                writer.Write(plane.State.OnGround);
            }
        });
    }

    public static Snapshot DecodeSnapshot(byte[] data) => Decode(data, reader =>
    {
        uint tick = reader.ReadUInt32();
        uint ack = reader.ReadUInt32();
        var match = new MatchStatus((MatchStage)reader.ReadByte(), reader.ReadByte(), reader.ReadSingle(), (Team)reader.ReadByte(), (EndReason)reader.ReadByte());
        float nukeReadyIn = reader.ReadSingle();
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
            int planeId = reader.ReadInt32();
            players[i] = new PlayerSnapshot(peer, new PlayerState(position, velocity, (flags & 1) != 0, (flags & 2) != 0), yaw, pitch, planeId);
        }

        int planeCount = reader.ReadByte();
        var planes = new PlaneSnapshot[planeCount];
        for (int i = 0; i < planeCount; i++)
        {
            int id = reader.ReadInt32();
            int pilot = reader.ReadInt32();
            var team = (Team)reader.ReadByte();
            Vector3 position = Read(reader);
            var rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var state = new PlaneState(position, rotation, reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadBoolean());
            planes[i] = new PlaneSnapshot(id, state, pilot, team);
        }

        return new Snapshot(tick, ack, players, planes, match, nukeReadyIn);
    });

    /// <summary>Block edits with their owners, 13 bytes each. Positions must fit in 0 to 65535, which every map does.</summary>
    public static byte[] EncodeEdits(IReadOnlyCollection<VoxelEdit> edits) => Encode(writer =>
    {
        writer.Write(edits.Count);
        foreach (VoxelEdit edit in edits)
        {
            writer.Write(checked((ushort)edit.Position.X));
            writer.Write(checked((ushort)edit.Position.Y));
            writer.Write(checked((ushort)edit.Position.Z));
            writer.Write((ushort)edit.Block);
            writer.Write(edit.Owner.PlayerId);
            writer.Write((byte)edit.Owner.Team);
        }
    });

    public static IReadOnlyList<VoxelEdit> DecodeEdits(byte[] data) => Decode(data, reader =>
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > (data.Length - 4) / 13)
        {
            throw new InvalidDataException($"Edit list claims {count} edits in {data.Length} bytes.");
        }

        var edits = new VoxelEdit[count];
        for (int i = 0; i < count; i++)
        {
            var position = new BlockPos(reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16());
            var block = (Block)reader.ReadUInt16();
            edits[i] = new VoxelEdit(position, block, new BlockOwner(reader.ReadInt32(), (Team)reader.ReadByte()));
        }

        return edits;
    });

    /// <summary>Whole objects (new or changed), each with its named values.</summary>
    public static byte[] EncodeObjects(IReadOnlyCollection<GameObject> objects) => Encode(writer =>
    {
        writer.Write(objects.Count);
        foreach (GameObject obj in objects)
        {
            writer.Write(obj.Id);
            writer.Write(obj.Kind);
            writer.Write((byte)obj.Team);
            writer.Write(obj.OwnerPlayerId);
            writer.Write(checked((ushort)obj.Origin.X));
            writer.Write(checked((ushort)obj.Origin.Y));
            writer.Write(checked((ushort)obj.Origin.Z));
            writer.Write((byte)obj.Facing);
            writer.Write(obj.Health);
            WriteValues(writer, obj.Values);
        }
    });

    public static IReadOnlyList<GameObject> DecodeObjects(byte[] data) => Decode(data, reader =>
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > data.Length)
        {
            throw new InvalidDataException($"Object list claims {count} objects.");
        }

        var objects = new GameObject[count];
        for (int i = 0; i < count; i++)
        {
            int id = reader.ReadInt32();
            string kind = reader.ReadString();
            var team = (Team)reader.ReadByte();
            int owner = reader.ReadInt32();
            var origin = new BlockPos(reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16());
            int facing = reader.ReadByte();
            int health = reader.ReadInt32();
            objects[i] = new GameObject(id, kind, team, owner, origin, facing & 3, health) { Values = ReadValues(reader, data.Length) };
        }

        return objects;
    });

    /// <summary>Item amounts by id (a player's backpack).</summary>
    public static byte[] EncodeCounts(IReadOnlyDictionary<string, int> counts) => Encode(writer => WriteValues(writer, counts));

    public static IReadOnlyDictionary<string, int> DecodeCounts(byte[] data) => Decode(data, reader => ReadValues(reader, data.Length));

    /// <summary>One team's state, for that team's players only.</summary>
    public static byte[] EncodeTeamState(TeamState state) => Encode(writer =>
    {
        writer.Write((byte)state.Team);
        WriteValues(writer, state.Stock);
        writer.Write(checked((byte)state.Era));
        WriteValues(writer, state.Points);
        WriteValues(writer, state.Researched.ToDictionary(tech => tech, _ => 1));
    });

    public static TeamState DecodeTeamState(byte[] data) => Decode(data, reader =>
        new TeamState((Team)reader.ReadByte())
        {
            Stock = ReadValues(reader, data.Length),
            Era = reader.ReadByte(),
            Points = ReadValues(reader, data.Length),
            Researched = ReadValues(reader, data.Length).Keys.ToList(),
        });

    /// <summary>Every connected player's name and team.</summary>
    public static byte[] EncodeRoster(IReadOnlyCollection<RosterEntry> roster) => Encode(writer =>
    {
        writer.Write(roster.Count);
        foreach (RosterEntry entry in roster)
        {
            writer.Write(entry.PeerId);
            writer.Write(entry.Name);
            writer.Write((byte)entry.Team);
            writer.Write(entry.ConcedeVote);
            writer.Write((ushort)Math.Clamp(entry.PingMs, 0, ushort.MaxValue));
            writer.Write(entry.PlayerId);
        }
    });

    public static IReadOnlyList<RosterEntry> DecodeRoster(byte[] data) => Decode(data, reader =>
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > data.Length)
        {
            throw new InvalidDataException($"Roster claims {count} players.");
        }

        var roster = new RosterEntry[count];
        for (int i = 0; i < count; i++)
        {
            roster[i] = new RosterEntry(reader.ReadInt32(), reader.ReadString(), (Team)reader.ReadByte(), reader.ReadBoolean(), reader.ReadUInt16(), reader.ReadInt32());
        }

        return roster;
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

    private static void WriteValues(BinaryWriter writer, IReadOnlyDictionary<string, int> values)
    {
        writer.Write(checked((ushort)values.Count));
        foreach ((string name, int value) in values)
        {
            writer.Write(name);
            writer.Write(value);
        }
    }

    private static IReadOnlyDictionary<string, int> ReadValues(BinaryReader reader, int messageLength)
    {
        int count = reader.ReadUInt16();
        if (count > messageLength)
        {
            throw new InvalidDataException($"Value list claims {count} values.");
        }

        var values = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            values[reader.ReadString()] = reader.ReadInt32();
        }

        return values;
    }

    private static Vector3 Read(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;

    private static float Clamp(float value, float min, float max) => Math.Clamp(Finite(value), min, max);
}
