using Godot;

namespace Dominatus.GodotTinyTown;

public sealed class DestinationVisualController
{
    private readonly Marker2D _marker;
    private readonly Node2D _visualRoot;
    private readonly List<CanvasItem> _fallbackItems = [];
    private readonly PanelContainer? _nameplate;
    private readonly Label? _nameLabel;
    private readonly Sprite2D _sprite;
    private readonly TinyTownDestinationPresentation _presentation;

    private TinyTownArtProfile _profile = new();
    private TinyTownSpriteCatalog? _catalog;
    private TinyTownVisualStatus _status = new(TinyTownVisualMode.FallbackShapes, TinyTownVisualMode.FallbackShapes, true, false);
    private string _loadedSpriteKey = string.Empty;

    public DestinationVisualController(Marker2D marker)
    {
        _marker = marker;
        _visualRoot = EnsureVisualRoot(marker);
        _nameplate = marker.GetNodeOrNull<PanelContainer>("Nameplate");
        _nameLabel = marker.GetNodeOrNull<Label>("Nameplate/NameLabel");
        _sprite = _visualRoot.GetNodeOrNull<Sprite2D>("Sprite2D") ?? new Sprite2D { Name = "Sprite2D", Centered = true, Visible = false };
        if (_sprite.GetParent() is null)
            _visualRoot.AddChild(_sprite);
        _sprite.RegionEnabled = true;
        _sprite.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        _sprite.Position = new Vector2(0f, -20f);

        _presentation = new TinyTownDestinationPresentation
        {
            Name = DetermineName(marker),
            Kind = DetermineKind(marker),
            AccentColor = DetermineAccent(DetermineKind(marker))
        };

        if (_nameLabel is not null)
            _nameLabel.Text = _presentation.Name;
    }

    public TinyTownDestinationPresentation Presentation => _presentation;

    public TinyTownVisualStatus Status => _status;

    public void Configure(TinyTownArtProfile profile, TinyTownSpriteCatalog catalog)
    {
        _profile = profile ?? new TinyTownArtProfile();
        _catalog = catalog;
        _loadedSpriteKey = string.Empty;
        Apply();
    }

    public void Apply()
    {
        if (_profile.EffectiveDestinationMode != TinyTownVisualMode.FallbackShapes
            && _catalog is not null
            && TryUseSprite())
        {
            return;
        }

        SetFallbackVisible(true);
        _sprite.Visible = false;
        _status = new(_profile.EffectiveDestinationMode, TinyTownVisualMode.FallbackShapes, true, false);
    }

    private bool TryUseSprite()
    {
        var key = _presentation.Kind.ToString();
        if (_loadedSpriteKey != key)
        {
            if (!_catalog!.TryGetDestinationSprite(_profile, _presentation, out var slice) || slice is null)
                return false;

            _sprite.Texture = slice.Texture;
            _sprite.RegionRect = slice.RegionRect;
            ApplySpriteScale(slice.RegionRect.Size);
            _sprite.Modulate = Colors.White;
            _loadedSpriteKey = key;
        }
        else if (_catalog!.TryGetDestinationSprite(_profile, _presentation, out var currentSlice) && currentSlice is not null)
        {
            _sprite.RegionRect = currentSlice.RegionRect;
        }

        SetFallbackVisible(false);
        _sprite.Visible = true;
        _status = new(_profile.EffectiveDestinationMode, TinyTownVisualMode.StaticSprites, false, true);
        return true;
    }

    private void ApplySpriteScale(Vector2 sourceSize)
    {
        if (sourceSize.Y <= 0f)
        {
            _sprite.Scale = Vector2.One;
            return;
        }

        var scale = _profile.DestinationTargetHeight / sourceSize.Y;
        _sprite.Scale = new Vector2(scale, scale);
    }

    private void SetFallbackVisible(bool visible)
    {
        foreach (var item in _fallbackItems)
            item.Visible = visible;

        if (_nameplate is not null)
            _nameplate.Visible = true;
    }

    private static Node2D EnsureVisualRoot(Marker2D marker)
    {
        var visualRoot = marker.GetNodeOrNull<Node2D>("VisualRoot");
        if (visualRoot is not null)
            return visualRoot;

        visualRoot = new Node2D { Name = "VisualRoot" };
        marker.AddChild(visualRoot);

        foreach (var child in marker.GetChildren())
        {
            if (child == visualRoot || child is not CanvasItem canvasItem)
                continue;

            if (string.Equals(child.Name, "Nameplate", StringComparison.Ordinal))
                continue;

            child.Reparent(visualRoot);
        }

        return visualRoot;
    }

    public void DiscoverFallbackItems()
    {
        _fallbackItems.Clear();
        foreach (var child in _visualRoot.GetChildren())
        {
            if (child is CanvasItem canvasItem && child != _sprite)
                _fallbackItems.Add(canvasItem);
        }
    }

    private static string DetermineName(Node marker)
    {
        return marker.Name.ToString() switch
        {
            "MayaHome" => "Maya Home",
            "TheoHome" => "Theo Home",
            "LinaHome" => "Lina Home",
            "NiaHome" => "Nia Home",
            "Well" => "Well",
            "Market" => "Market",
            "Garden" => "Garden",
            _ => marker.Name.ToString()
        };
    }

    private static TinyTownDestinationKind DetermineKind(Node marker)
    {
        var name = marker.Name.ToString();
        if (name.EndsWith("Home", StringComparison.Ordinal))
            return TinyTownDestinationKind.Home;

        return name switch
        {
            "Well" => TinyTownDestinationKind.Well,
            "Market" => TinyTownDestinationKind.Market,
            "Garden" => TinyTownDestinationKind.Garden,
            _ => TinyTownDestinationKind.Unknown
        };
    }

    private static Color DetermineAccent(TinyTownDestinationKind kind) => kind switch
    {
        TinyTownDestinationKind.Home => new Color("8a6c52"),
        TinyTownDestinationKind.Well => new Color("5f9ecf"),
        TinyTownDestinationKind.Market => new Color("eea04f"),
        TinyTownDestinationKind.Garden => new Color("4f8b57"),
        _ => new Color("7b8ea2")
    };
}
