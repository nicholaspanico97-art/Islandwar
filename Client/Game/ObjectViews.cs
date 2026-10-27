using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Client.Ui;
using IslandWar.Sim.Items;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Game;

/// <summary>
/// Draws every structure as a coloured box over its footprint (hiding the plain structure blocks under it)
/// with its name and team above (a blueprint as a see-through box, filling up as its cost is delivered), and a see-through preview of where the selected structure would go. Kept
/// in step with the link's object store each frame: objects are replaced whole when they change, so a
/// changed reference means redraw.
/// </summary>
public partial class ObjectViews : Node3D
{
    // A hair bigger than the footprint so the box's faces win over the blocks' faces.
    private const float Grow = 0.02f;

    private readonly Dictionary<int, (GameObject Shown, Node3D Node)> _views = new();
    private MeshInstance3D _preview = null!;
    private StandardMaterial3D _previewMaterial = null!;

    public ObjectStore? Objects { get; set; }

    public override void _Ready()
    {
        Name = "Objects";
        _previewMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _preview = new MeshInstance3D { Name = "Preview", Mesh = new BoxMesh(), MaterialOverride = _previewMaterial, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_preview);
    }

    public override void _Process(double delta)
    {
        if (Objects is null)
        {
            return;
        }

        foreach (int gone in _views.Keys.Where(id => Objects.Get(id) is null).ToList())
        {
            _views[gone].Node.QueueFree();
            _views.Remove(gone);
        }

        foreach (GameObject obj in Objects.All)
        {
            if (_views.TryGetValue(obj.Id, out var view) && ReferenceEquals(view.Shown, obj))
            {
                continue;
            }

            view.Node?.QueueFree();
            _views[obj.Id] = (obj, Build(obj, Objects.KindOf(obj)));
        }
    }

    /// <summary>Shows where a structure would go, green if it fits and red if not; null hides it.</summary>
    public void ShowPreview(ObjectKind? kind, BlockPos origin, int facing, bool fits)
    {
        _preview.Visible = kind is not null;
        if (kind is null)
        {
            return;
        }

        Vector3 size = SizeOf(kind, facing);
        ((BoxMesh)_preview.Mesh).Size = size + Vector3.One * Grow * 2;
        _preview.Position = new Vector3(origin.X, origin.Y, origin.Z) + size / 2;
        _previewMaterial.AlbedoColor = fits ? new Color(0.3f, 1, 0.4f, 0.35f) : new Color(1, 0.25f, 0.2f, 0.35f);
    }

    private Node3D Build(GameObject obj, ObjectKind kind)
    {
        Vector3 size = SizeOf(kind, obj.Facing);
        var node = new Node3D { Name = $"Object{obj.Id}", Position = new Vector3(obj.Origin.X, obj.Origin.Y, obj.Origin.Z) + size / 2 };

        if (obj.IsBlueprint())
        {
            BuildBlueprint(node, obj, kind, size);
            AddChild(node);
            return node;
        }

        // Darker as it takes damage, so a hurt structure is visible from the air.
        float health = Mathf.Clamp((float)obj.Health / kind.MaxHealth, 0, 1);
        Color colour = new Color(kind.Color).Darkened(0.5f * (1 - health));
        node.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size + Vector3.One * Grow * 2 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.8f },
        });

        // A band in the team's colour round the top, to tell whose it is at a glance.
        node.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size.X + Grow * 4, 0.2f, size.Z + Grow * 4) },
            Position = new Vector3(0, size.Y / 2 - 0.1f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = TeamColors.Of(obj.Team) },
        });

        string damage = obj.Health < kind.MaxHealth ? $"\n{obj.Health} / {kind.MaxHealth}" : "";
        // A dock says how far along the ship it's building is.
        if (Objects!.Kinds.ShipBuiltAt(kind) is { } ship && Blueprint.Delivered(obj).Count > 0)
        {
            damage += $"\n{ship.Name}: {Mathf.RoundToInt(Blueprint.Progress(obj, kind, Objects.Kinds) * 100)}% delivered";
        }
        node.AddChild(new Label3D
        {
            Text = $"{kind.Name}{damage}",
            Position = new Vector3(0, size.Y / 2 + 0.6f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = TeamColors.Of(obj.Team).Lightened(0.4f),
            OutlineSize = 8,
            PixelSize = 0.01f,
            FontSize = 48,
        });
        AddChild(node);
        return node;
    }

    /// <summary>
    /// A blueprint: a pale see-through shell in the team's colour, a solid part filling up from the bottom as
    /// its cost is delivered, and what it still needs above.
    /// </summary>
    private void BuildBlueprint(Node3D node, GameObject obj, ObjectKind kind, Vector3 size)
    {
        Color team = TeamColors.Of(obj.Team);
        node.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size + Vector3.One * Grow * 2 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(team.Lightened(0.5f), 0.25f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        float done = Mathf.Clamp(Blueprint.Progress(obj, kind, Objects!.Kinds), 0, 1);
        if (done > 0)
        {
            float height = size.Y * done;
            node.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(size.X, height, size.Z) },
                Position = new Vector3(0, (height - size.Y) / 2, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(kind.Color), Roughness = 0.8f },
            });
        }

        node.AddChild(new Label3D
        {
            Text = $"{kind.Name} (blueprint)\n{Mathf.RoundToInt(done * 100)}% delivered",
            Position = new Vector3(0, size.Y / 2 + 0.6f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = team.Lightened(0.4f),
            OutlineSize = 8,
            PixelSize = 0.01f,
            FontSize = 48,
        });
    }

    private static Vector3 SizeOf(ObjectKind kind, int facing) =>
        facing % 2 == 0 ? new Vector3(kind.Size.X, kind.Size.Y, kind.Size.Z) : new Vector3(kind.Size.Z, kind.Size.Y, kind.Size.X);
}
