using IslandWar.Sim.Match;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Objects;

/// <summary>
/// Something in the world that isn't terrain and has state of its own: for now the structures players place
/// (depots, workshops and, with the tech tree, machines, launchers and the rest). The server owns every
/// object; clients get each one whole when it appears or changes, and every one on joining.
/// </summary>
/// <remarks>
/// A structure is anchored to the block grid: it fills its footprint with <see cref="Block.Structure"/> so
/// players collide with it and bombs land on it, and those blocks only go away with the structure. Anything
/// a kind needs to remember (stock, progress, fuel) goes in <see cref="Values"/>, so new kinds need no new
/// message types.
/// </remarks>
/// <param name="Id">Unique while the server runs; never reused. 0 means none.</param>
/// <param name="Kind">An <see cref="ObjectKind.Id"/>.</param>
/// <param name="OwnerPlayerId">The player who placed it (team build protection applies to it like a block).</param>
/// <param name="Origin">The footprint's lowest corner.</param>
/// <param name="Facing">0 to 3: north, west, south, east (heading in quarter turns, like a plane's).</param>
public sealed record GameObject(int Id, string Kind, Team Team, int OwnerPlayerId, BlockPos Origin, int Facing, int Health)
{
    private static readonly IReadOnlyDictionary<string, int> NoValues = new SortedDictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Named numbers the object's kind keeps, in name order.</summary>
    public IReadOnlyDictionary<string, int> Values { get; init; } = NoValues;

    public BlockOwner Owner => new(OwnerPlayerId, Team);

    /// <summary>A copy with one value set (0 removes it).</summary>
    public GameObject With(string name, int value)
    {
        var values = new SortedDictionary<string, int>(Values.ToDictionary(v => v.Key, v => v.Value), StringComparer.Ordinal);
        if (value == 0)
        {
            values.Remove(name);
        }
        else
        {
            values[name] = value;
        }

        return this with { Values = values };
    }

    /// <summary>The blocks it fills.</summary>
    public IEnumerable<BlockPos> Footprint(ObjectKind kind) => FootprintOf(kind, Origin, Facing);

    public static IEnumerable<BlockPos> FootprintOf(ObjectKind kind, BlockPos origin, int facing)
    {
        (int sizeX, int sizeZ) = facing % 2 == 0 ? (kind.Size.X, kind.Size.Z) : (kind.Size.Z, kind.Size.X);
        for (int y = 0; y < kind.Size.Y; y++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    yield return new BlockPos(origin.X + x, origin.Y + y, origin.Z + z);
                }
            }
        }
    }
}
