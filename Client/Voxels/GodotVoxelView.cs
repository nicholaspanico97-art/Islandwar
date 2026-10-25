using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Voxels;

/// <summary>
/// Renders a Sim <see cref="VoxelWorld"/> with godot_voxel's <c>VoxelTerrain</c> (GDExtension edition).
/// The Sim world stays authoritative: this node copies every chunk in once, then mirrors each edit
/// through <see cref="ShowEdit"/>. godot_voxel also builds the collision shapes the player walks on.
/// </summary>
/// <remarks>
/// The GDExtension has no C# bindings, so every godot_voxel class, property and method is reached by
/// string name through <c>ClassDB.Instantiate</c>, <c>Set</c> and <c>Call</c>. Keep all of that in this file.
/// </remarks>
public partial class GodotVoxelView : Node3D
{
    // VoxelBuffer.CHANNEL_TYPE. Its default depth is 16 bits, which matches Block's ushort values.
    private const int ChannelType = 0;

    private readonly Queue<ChunkPos> _unsent = new();
    private Node3D _terrain = null!;
    private GodotObject _tool = null!;

    public VoxelWorld World { get; init; } = null!;

    /// <summary>True once every chunk has been handed to godot_voxel. Meshing finishes a few frames later.</summary>
    public bool AllChunksSent => _unsent.Count == 0;

    public override void _Ready()
    {
        Name = "VoxelView";

        var mesher = ClassDB.Instantiate("VoxelMesherBlocky").AsGodotObject();
        mesher.Set("library", CreateLibrary());

        _terrain = (Node3D)ClassDB.Instantiate("VoxelTerrain").AsGodotObject();
        _terrain.Name = "VoxelTerrain";
        _terrain.Set("mesher", mesher);
        _terrain.Set("automatic_loading_enabled", false);
        _terrain.Set("bounds", new Aabb(Vector3.Zero, new Vector3(World.SizeX, World.SizeY, World.SizeZ)));
        _terrain.Set("generate_collisions", true);
        _terrain.Set("material_override", new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true });
        AddChild(_terrain);

        // VoxelTerrain only accepts blocks inside a viewer's range, so one viewer at the centre covers the
        // whole bounded map.
        var viewer = (Node3D)ClassDB.Instantiate("VoxelViewer").AsGodotObject();
        viewer.Name = "VoxelViewer";
        viewer.Position = new Vector3(World.SizeX, World.SizeY, World.SizeZ) / 2;
        viewer.Set("view_distance", Mathf.CeilToInt(new Vector3(World.SizeX, World.SizeY, World.SizeZ).Length() / 2) + Chunk.Size);
        AddChild(viewer);

        _tool = _terrain.Call("get_voxel_tool").AsGodotObject();
        _tool.Set("channel", ChannelType);

        foreach (Chunk chunk in World.Chunks)
        {
            _unsent.Enqueue(chunk.Position);
        }
    }

    public override void _Process(double delta)
    {
        // The viewer pairs with the terrain during the terrain's own processing, so the first frames can
        // refuse blocks. Keep retrying until they're all accepted.
        while (_unsent.Count > 0)
        {
            ChunkPos position = _unsent.Peek();
            if (!_terrain.Call("try_set_block_data", new Vector3I(position.X, position.Y, position.Z), ToVoxelBuffer(World.GetChunk(position)!)).AsBool())
            {
                return;
            }

            _unsent.Dequeue();
        }
    }

    /// <summary>
    /// True once godot_voxel has meshed every chunk touching <paramref name="area"/> (in blocks). Meshing
    /// also builds the collision shapes, so after this the player can stand there.
    /// </summary>
    public bool IsAreaMeshed(Aabb area) => AllChunksSent && _terrain.Call("is_area_meshed", area).AsBool();

    /// <summary>Shows a block change that has already been applied to <see cref="World"/>.</summary>
    public void ShowEdit(VoxelEdit edit)
    {
        _tool.Call("set_voxel", new Vector3I(edit.Position.X, edit.Position.Y, edit.Position.Z), (int)edit.Block);
    }

    private static GodotObject CreateLibrary()
    {
        var library = ClassDB.Instantiate("VoxelBlockyLibrary").AsGodotObject();
        // The library's model index must equal the block id, so add one model per id in order.
        library.Call("add_model", ClassDB.Instantiate("VoxelBlockyModelEmpty"));
        foreach (Block block in Enum.GetValues<Block>().Where(b => b != Block.Air).Order())
        {
            var cube = ClassDB.Instantiate("VoxelBlockyModelCube").AsGodotObject();
            cube.Set("color", BlockColors.Of(block));
            if (library.Call("add_model", cube).AsInt32() != (int)block)
            {
                throw new InvalidOperationException($"Block ids must be contiguous; {block} would render as the wrong model.");
            }
        }

        library.Call("bake");
        return library;
    }

    /// <summary>Copies a chunk into a new VoxelBuffer. VoxelBuffer stores voxels in ZXY order, little-endian.</summary>
    private static GodotObject ToVoxelBuffer(Chunk chunk)
    {
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

        var buffer = ClassDB.Instantiate("VoxelBuffer").AsGodotObject();
        buffer.Call("create", Chunk.Size, Chunk.Size, Chunk.Size);
        buffer.Call("set_channel_from_byte_array", ChannelType, bytes);
        return buffer;
    }
}
