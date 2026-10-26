using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Objects;

/// <summary>
/// Every object in the world by id, and which object fills each block. The server and every client keep
/// one; the client's is a copy of the server's, kept up to date by the messages it sends.
/// </summary>
public sealed class ObjectStore
{
    private readonly SortedDictionary<int, GameObject> _objects = new();
    private readonly Dictionary<BlockPos, int> _cells = new();

    public ObjectStore(ObjectKinds kinds)
    {
        Kinds = kinds;
    }

    public ObjectKinds Kinds { get; }

    /// <summary>In id order.</summary>
    public IEnumerable<GameObject> All => _objects.Values;

    public int Count => _objects.Count;

    public GameObject? Get(int id) => _objects.TryGetValue(id, out GameObject? found) ? found : null;

    /// <summary>The object filling a block, if any.</summary>
    public GameObject? At(BlockPos block) => _cells.TryGetValue(block, out int id) ? _objects[id] : null;

    public ObjectKind KindOf(GameObject obj) =>
        Kinds.Get(obj.Kind) ?? throw new InvalidDataException($"Object {obj.Id} has unknown kind '{obj.Kind}'.");

    /// <summary>Adds an object, or replaces the one with the same id.</summary>
    public void Set(GameObject obj)
    {
        Remove(obj.Id);
        _objects[obj.Id] = obj;
        foreach (BlockPos cell in obj.Footprint(KindOf(obj)))
        {
            _cells[cell] = obj.Id;
        }
    }

    public bool Remove(int id)
    {
        if (!_objects.Remove(id, out GameObject? old))
        {
            return false;
        }

        foreach (BlockPos cell in old.Footprint(KindOf(old)))
        {
            _cells.Remove(cell);
        }

        return true;
    }

    public void Clear()
    {
        _objects.Clear();
        _cells.Clear();
    }

    /// <summary>A 64-bit FNV-1a hash of every object, in id order, so a client can check its copy.</summary>
    public ulong ComputeHash()
    {
        ulong hash = 14695981039346656037;
        void Mix(int value)
        {
            for (int i = 0; i < 4; i++)
            {
                hash = (hash ^ (byte)(value >> (8 * i))) * 1099511628211;
            }
        }

        void MixText(string text)
        {
            Mix(text.Length);
            foreach (char c in text)
            {
                Mix(c);
            }
        }

        foreach (GameObject obj in _objects.Values)
        {
            Mix(obj.Id);
            MixText(obj.Kind);
            Mix((int)obj.Team);
            Mix(obj.OwnerPlayerId);
            Mix(obj.Origin.X);
            Mix(obj.Origin.Y);
            Mix(obj.Origin.Z);
            Mix(obj.Facing);
            Mix(obj.Health);
            Mix(obj.Values.Count);
            foreach ((string name, int value) in obj.Values)
            {
                MixText(name);
                Mix(value);
            }
        }

        return hash;
    }
}
