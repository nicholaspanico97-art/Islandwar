using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Voxels;

/// <summary>
/// Renders a Sim <see cref="VoxelWorld"/> with godot_voxel's <c>VoxelTerrain</c> (GDExtension edition).
/// The Sim world stays authoritative and holds the whole map; this node only draws the part around
/// <see cref="Focus"/> (the camera): it copies chunks in nearest first as they come into range, godot_voxel
/// drops them again as they leave it, and each edit is mirrored through <see cref="ShowEdit"/>. It only
/// draws: player collision is done in Sim (PlayerMovement).
/// </summary>
/// <remarks>
/// The GDExtension has no C# bindings, so every godot_voxel class, property and method is reached by
/// string name through <c>ClassDB.Instantiate</c>, <c>Set</c> and <c>Call</c>. Keep all of that in this file.
/// </remarks>
public partial class GodotVoxelView : Node3D
{
    // VoxelBuffer.CHANNEL_TYPE. Its default depth is 16 bits, which matches Block's ushort values.
    private const int ChannelType = 0;

    /// <summary>After godot_voxel refuses a chunk (it hasn't caught up with the viewer yet), replan this much later.</summary>
    private const double RetryDelay = 0.25;

    private readonly HashSet<ChunkPos> _sent = new();

    /// <summary>Chunks in range that godot_voxel doesn't have yet, farthest first so the nearest is at the end.</summary>
    private readonly List<ChunkPos> _missing = new();

    private Node3D _terrain = null!;
    private Node3D _viewer = null!;
    private GodotObject _tool = null!;
    private int _viewDistance;
    private Vector3I _plannedAt = new(int.MinValue, 0, 0);
    private int _plannedDistance;
    private bool _retry;
    private double _retryIn;

    public VoxelWorld World { get; init; } = null!;

    public ViewSettings Settings { get; init; } = null!;

    /// <summary>Where the camera is. Terrain is drawn out to <see cref="ViewDistance"/> around it.</summary>
    public Vector3 Focus { get; set; }

    /// <summary>How far to draw, in blocks; the game raises it when flying.</summary>
    public int ViewDistance
    {
        get => _viewDistance;
        set
        {
            _viewDistance = Math.Clamp(value, Chunk.Size, ViewSettings.MaxDistance);
            _viewer?.Set("view_distance", _viewDistance);
        }
    }

    /// <summary>True when every chunk in range has been handed to godot_voxel. Meshing finishes a few frames later.</summary>
    public bool CaughtUp => _missing.Count == 0 && !_retry;

    public override void _Ready()
    {
        Name = "VoxelView";

        var mesher = ClassDB.Instantiate("VoxelMesherBlocky").AsGodotObject();
        mesher.Set("library", CreateLibrary());

        _terrain = (Node3D)ClassDB.Instantiate("VoxelTerrain").AsGodotObject();
        _terrain.Name = "VoxelTerrain";
        _terrain.Set("mesher", mesher);
        _terrain.Set("automatic_loading_enabled", false); // chunks come from World, not a generator
        _terrain.Set("bounds", new Aabb(Vector3.Zero, new Vector3(World.SizeX, World.SizeY, World.SizeZ)));
        _terrain.Set("generate_collisions", false); // Sim does player collision (PlayerMovement).
        _terrain.Set("max_view_distance", ViewSettings.MaxDistance); // the terrain clamps viewers to this (default 128)
        _terrain.Set("material_override", new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true });
        _terrain.Connect("block_unloaded", Callable.From<Vector3I>(OnBlockUnloaded));
        AddChild(_terrain);

        // VoxelTerrain only accepts and keeps blocks inside a viewer's range; the viewer follows the camera.
        _viewer = (Node3D)ClassDB.Instantiate("VoxelViewer").AsGodotObject();
        _viewer.Name = "VoxelViewer";
        _viewer.Position = Focus;
        AddChild(_viewer);
        ViewDistance = _viewDistance > 0 ? _viewDistance : Settings.ViewDistance;

        _tool = _terrain.Call("get_voxel_tool").AsGodotObject();
        _tool.Set("channel", ChannelType);
    }

    public override void _Process(double delta)
    {
        _viewer.Position = Focus;
        var centre = new Vector3I(
            Mathf.FloorToInt(Focus.X / Chunk.Size), Mathf.FloorToInt(Focus.Y / Chunk.Size), Mathf.FloorToInt(Focus.Z / Chunk.Size));

        _retryIn -= delta;
        if (centre != _plannedAt || _viewDistance != _plannedDistance || (_retry && _retryIn <= 0))
        {
            Plan(centre);
        }

        int sent = 0;
        while (sent < Settings.ChunksPerFrame && _missing.Count > 0)
        {
            ChunkPos next = _missing[^1];
            _missing.RemoveAt(_missing.Count - 1);
            if (_sent.Contains(next))
            {
                continue;
            }

            // The viewer pairs with the terrain, and the terrain moves its range, during the terrain's own
            // processing, so it can refuse chunks for a frame or two. Try again shortly.
            if (!_terrain.Call("try_set_block_data", new Vector3I(next.X, next.Y, next.Z), ToVoxelBuffer(World.GetChunk(next)!)).AsBool())
            {
                _retry = true;
                _retryIn = RetryDelay;
                break;
            }

            _sent.Add(next);
            sent++;
        }
    }

    /// <summary>
    /// Shows a block change that has already been applied to <see cref="World"/>. A chunk godot_voxel doesn't
    /// have needs nothing: it's copied from <see cref="World"/>, edits and all, when it comes into range.
    /// </summary>
    public void ShowEdit(VoxelEdit edit)
    {
        if (_sent.Contains(ChunkPos.Containing(edit.Position)))
        {
            _tool.Call("set_voxel", new Vector3I(edit.Position.X, edit.Position.Y, edit.Position.Z), (int)edit.Block);
        }
    }

    /// <summary>
    /// Lists the chunks within the view distance of the camera (horizontally; the world is only a few chunks
    /// tall) that godot_voxel doesn't have, nearest last.
    /// </summary>
    private void Plan(Vector3I centre)
    {
        _plannedAt = centre;
        _plannedDistance = _viewDistance;
        _retry = false;
        _missing.Clear();

        int radius = Mathf.CeilToInt(_viewDistance / (float)Chunk.Size);
        int reach = (radius * 2 + 1) * (radius * 2 + 1) / 4;
        for (int z = Math.Max(0, centre.Z - radius); z <= Math.Min(World.ChunksZ - 1, centre.Z + radius); z++)
        {
            for (int x = Math.Max(0, centre.X - radius); x <= Math.Min(World.ChunksX - 1, centre.X + radius); x++)
            {
                if ((x - centre.X) * (x - centre.X) + (z - centre.Z) * (z - centre.Z) > reach)
                {
                    continue;
                }

                for (int y = Math.Max(0, centre.Y - radius); y <= Math.Min(World.ChunksY - 1, centre.Y + radius); y++)
                {
                    var position = new ChunkPos(x, y, z);
                    if (!_sent.Contains(position))
                    {
                        _missing.Add(position);
                    }
                }
            }
        }

        _missing.Sort((a, b) => Distance(b, centre).CompareTo(Distance(a, centre)));
    }

    private static int Distance(ChunkPos chunk, Vector3I centre) =>
        (chunk.X - centre.X) * (chunk.X - centre.X) + (chunk.Y - centre.Y) * (chunk.Y - centre.Y) + (chunk.Z - centre.Z) * (chunk.Z - centre.Z);

    private void OnBlockUnloaded(Vector3I position)
    {
        _sent.Remove(new ChunkPos(position.X, position.Y, position.Z));
    }

    private static GodotObject CreateLibrary()
    {
        var library = ClassDB.Instantiate("VoxelBlockyLibrary").AsGodotObject();
        // The library's model index must equal the block id, so add one model per id in order.
        library.Call("add_model", ClassDB.Instantiate("VoxelBlockyModelEmpty"));
        foreach (Block block in Enum.GetValues<Block>().Where(b => b != Block.Air).Order())
        {
            // A structure's blocks aren't drawn: it draws its own model (a blueprint's is see-through), and the
            // ground around them keeps its faces.
            GodotObject model = ClassDB.Instantiate(block == Block.Structure ? "VoxelBlockyModelEmpty" : "VoxelBlockyModelCube").AsGodotObject();
            if (block != Block.Structure)
            {
                model.Set("color", BlockColors.Of(block));
            }

            if (library.Call("add_model", model).AsInt32() != (int)block)
            {
                throw new InvalidOperationException($"Block ids must be contiguous; {block} would render as the wrong model.");
            }
        }

        library.Call("bake");
        return library;
    }

    /// <summary>
    /// Copies a chunk into a new VoxelBuffer: a fill for a chunk of one block type, otherwise every voxel
    /// (VoxelBuffer stores them in ZXY order, little-endian).
    /// </summary>
    private static GodotObject ToVoxelBuffer(Chunk chunk)
    {
        var buffer = ClassDB.Instantiate("VoxelBuffer").AsGodotObject();
        buffer.Call("create", Chunk.Size, Chunk.Size, Chunk.Size);
        if (chunk.IsUniform)
        {
            buffer.Call("fill", (int)chunk.UniformBlock, ChannelType);
            return buffer;
        }

        var bytes = new byte[Chunk.Volume * sizeof(ushort)];
        for (int y = 0; y < Chunk.Size; y++)
        {
            for (int z = 0; z < Chunk.Size; z++)
            {
                for (int x = 0; x < Chunk.Size; x++)
                {
                    ushort value = (ushort)chunk.Get(x, y, z);
                    int i = 2 * (y + Chunk.Size * (x + Chunk.Size * z));
                    bytes[i] = (byte)value;
                    bytes[i + 1] = (byte)(value >> 8);
                }
            }
        }

        buffer.Call("set_channel_from_byte_array", ChannelType, bytes);
        return buffer;
    }
}
