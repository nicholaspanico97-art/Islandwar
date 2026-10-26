using System.Numerics;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

public class PredictedPlayerTests
{
    private const float Dt = 1f / 60;
    private static readonly PlayerSettings Settings = new();
    private static readonly GameRules Rules = new(Settings, new PlaneSettings(), new IslandWar.Sim.Weapons.BombSettings(), 0);

    private static PawnState At(Vector3 feet) => PawnState.OnFoot(PlayerState.At(feet));

    private static VoxelWorld FloorWorld()
    {
        var world = new VoxelWorld(2, 1, 2);
        for (int z = 0; z < world.SizeZ; z++)
        {
            for (int x = 0; x < world.SizeX; x++)
            {
                world.SetGenerated(new BlockPos(x, 3, z), Block.Stone);
            }
        }

        return world;
    }

    private static readonly PlayerInput Forward = new(0, 0, -1, 0, 0, InputButtons.None);

    [Fact]
    public void InputsGetIncreasingSequenceNumbers()
    {
        var player = new PredictedPlayer(At(new Vector3(8.5f, 4, 20.5f)));
        VoxelWorld world = FloorWorld();

        uint first = player.Predict(world, Rules, Forward, Dt).Seq;
        uint second = player.Predict(world, Rules, Forward, Dt).Seq;

        Assert.Equal(first + 1, second);
        Assert.Equal(new[] { first, second }, player.Latest(4).Select(i => i.Seq));
    }

    [Fact]
    public void AgreeingServerCausesNoCorrection()
    {
        VoxelWorld world = FloorWorld();
        PawnState start = At(new Vector3(8.5f, 4, 20.5f));
        var client = new PredictedPlayer(start);
        PawnState server = start;

        // The server has processed the first 10 of 20 inputs when its state arrives.
        for (int i = 0; i < 20; i++)
        {
            PlayerInput sent = client.Predict(world, Rules, Forward, Dt);
            if (i < 10)
            {
                server = Pawn.Step(world, Rules, server, sent, Dt);
            }
        }

        float correction = client.Reconcile(world, Rules, server, 10, Dt);

        Assert.Equal(0, correction, 4);
        Assert.Equal(10, client.PendingCount);
    }

    [Fact]
    public void DisagreeingServerWinsAndUnackedInputsReplayOnTop()
    {
        VoxelWorld world = FloorWorld();
        var client = new PredictedPlayer(At(new Vector3(8.5f, 4, 20.5f)));
        for (int i = 0; i < 20; i++)
        {
            client.Predict(world, Rules, Forward, Dt);
        }

        // The server put the player somewhere else (a respawn, say) after input 10.
        PawnState serverState = At(new Vector3(20.5f, 4, 20.5f));
        float correction = client.Reconcile(world, Rules, serverState, 10, Dt);

        Assert.True(correction > 5);
        Assert.Equal(20.5f, client.State.Player.Position.X, 3);
        Assert.Equal(20.5f - 10 * Dt * Settings.WalkSpeed, client.State.Player.Position.Z, 2);
    }

    [Fact]
    public void StaleAcknowledgementsAreIgnored()
    {
        VoxelWorld world = FloorWorld();
        var client = new PredictedPlayer(At(new Vector3(8.5f, 4, 20.5f)));
        for (int i = 0; i < 5; i++)
        {
            client.Predict(world, Rules, Forward, Dt);
        }

        client.Reconcile(world, Rules, client.State, 5, Dt);
        PawnState before = client.State;

        Assert.Equal(0, client.Reconcile(world, Rules, At(Vector3.Zero), 3, Dt));
        Assert.Equal(before, client.State);
    }
}

public class InterpolationBufferTests
{
    private static RemoteState At(float x, float yaw = 0) => new(new Vector3(x, 0, 0), yaw, 0, false, false);

    [Fact]
    public void SamplesBlendBetweenSnapshots()
    {
        var buffer = InterpolationBuffers.ForPlayer();
        buffer.Add(10, At(0));
        buffer.Add(13, At(3));

        Assert.Equal(1, buffer.Sample(11).Position.X, 4);
        Assert.Equal(2.5f, buffer.Sample(12.5).Position.X, 4);
    }

    [Fact]
    public void SamplesOutsideTheRangeHoldTheEnds()
    {
        var buffer = InterpolationBuffers.ForPlayer();
        buffer.Add(10, At(0));
        buffer.Add(13, At(3));

        Assert.Equal(0, buffer.Sample(5).Position.X);
        Assert.Equal(3, buffer.Sample(99).Position.X);
    }

    [Fact]
    public void OutOfOrderSnapshotsAreDropped()
    {
        var buffer = InterpolationBuffers.ForPlayer();
        buffer.Add(13, At(3));
        buffer.Add(10, At(0));

        Assert.Equal(3, buffer.Sample(11).Position.X);
    }

    [Fact]
    public void YawTakesTheShortWayRound()
    {
        float from = MathF.PI - 0.1f;
        float to = -MathF.PI + 0.1f;

        float halfway = InterpolationBuffers.LerpAngle(from, to, 0.5f);

        Assert.Equal(MathF.PI, MathF.Abs(halfway), 3);
    }
}

public class WireFormatTests
{
    [Fact]
    public void InputsRoundTrip()
    {
        var inputs = new[]
        {
            new PlayerInput(7, 0.5f, -1, 1.25f, -0.3f, InputButtons.Jump | InputButtons.Fly),
            new PlayerInput(8, 0, 0, -3, 0, InputButtons.None),
        };

        Assert.Equal(inputs, WireFormat.DecodeInputs(WireFormat.EncodeInputs(inputs)));
    }

    [Fact]
    public void HostileInputValuesAreClamped()
    {
        var input = new PlayerInput(1, 50, float.NaN, float.PositiveInfinity, 9, (InputButtons)0xFF); // includes an unknown bit 128

        PlayerInput decoded = Assert.Single(WireFormat.DecodeInputs(WireFormat.EncodeInputs(new[] { input })));

        Assert.Equal(1, decoded.MoveX);
        Assert.Equal(0, decoded.MoveZ);
        Assert.Equal(0, decoded.Yaw);
        Assert.Equal(MathF.PI / 2, decoded.Pitch, 4);
        Assert.Equal(InputButtons.Jump | InputButtons.Sprint | InputButtons.Descend | InputButtons.Fly | InputButtons.Use | InputButtons.Fire | InputButtons.FireNuke, decoded.Buttons);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 0, 0 })]
    [InlineData(new byte[] { 200 })]
    public void MalformedInputPacketsAreRejected(byte[] data)
    {
        Assert.Throws<InvalidDataException>(() => WireFormat.DecodeInputs(data));
    }

    [Fact]
    public void SnapshotsRoundTrip()
    {
        var snapshot = new Snapshot(1234, 99, new[]
        {
            new PlayerSnapshot(42, new PlayerState(new Vector3(1, 2, 3), new Vector3(0, -4, 0.5f), true, false), 0.7f, -0.2f),
            new PlayerSnapshot(7, new PlayerState(new Vector3(9, 8, 7), Vector3.Zero, false, true), -1, 0.1f, PlaneId: 3),
        }, new[]
        {
            new PlaneSnapshot(3, new PlaneState(new Vector3(5, 30, 6), Quaternion.CreateFromYawPitchRoll(0.3f, 0.1f, -0.4f), 31, 0.5f, 0.8f, false), 7, IslandWar.Sim.Match.Team.Blue),
        }, new IslandWar.Sim.Match.MatchStatus(IslandWar.Sim.Match.MatchStage.Playing, 2, 612.5f, IslandWar.Sim.Match.Team.None, IslandWar.Sim.Match.EndReason.None));

        Snapshot decoded = WireFormat.DecodeSnapshot(WireFormat.EncodeSnapshot(snapshot));

        Assert.Equal(snapshot.Tick, decoded.Tick);
        Assert.Equal(snapshot.AckSeq, decoded.AckSeq);
        Assert.Equal(snapshot.Players, decoded.Players);
        Assert.Equal(snapshot.Planes, decoded.Planes);
        Assert.Equal(snapshot.Match, decoded.Match);
    }

    [Fact]
    public void EditsRoundTrip()
    {
        var edits = new[] { new VoxelEdit(new BlockPos(0, 63, 127), Block.Planks), new VoxelEdit(new BlockPos(65535, 0, 1), Block.Air) };

        Assert.Equal(edits, WireFormat.DecodeEdits(WireFormat.EncodeEdits(edits)));
    }

    [Fact]
    public void EditCountLargerThanThePayloadIsRejected()
    {
        byte[] data = WireFormat.EncodeEdits(new[] { new VoxelEdit(new BlockPos(1, 1, 1), Block.Stone) });
        data[0] = 200;

        Assert.Throws<InvalidDataException>(() => WireFormat.DecodeEdits(data));
    }

    [Fact]
    public void NetDataFileLoads()
    {
        NetSettings settings = NetSettings.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "net.json")));

        Assert.True(settings.TickRate > 0);
        Assert.True(settings.InputsPerPacket <= WireFormat.MaxInputsPerPacket);
    }
}
