using Godot;
using System.Linq;

namespace Dominatus.GodotTinyTown;

public sealed class VillagerVisualController
{
    private readonly Node2D _visualRoot;
    private readonly CanvasItem? _shadow;
    private readonly Polygon2D _body;
    private readonly Polygon2D _head;
    private readonly Polygon2D _accent;
    private readonly Polygon2D _facingIndicator;
    private readonly Sprite2D _sprite;
    private readonly AnimatedSprite2D _animatedSprite;

    private TinyTownArtProfile _profile = new();
    private TinyTownSpriteCatalog? _catalog;
    private TinyTownVisualStatus _status = new(TinyTownVisualMode.FallbackShapes, TinyTownVisualMode.FallbackShapes, true, false);
    private string _loadedSpriteKey = string.Empty;
    private string _loadedFramesKey = string.Empty;

    public VillagerVisualController(Node2D visualRoot)
    {
        _visualRoot = visualRoot;
        _shadow = visualRoot.GetNodeOrNull<CanvasItem>("Shadow");
        _body = visualRoot.GetNodeOrNull<Polygon2D>("Body") ?? CreateBody(visualRoot);
        _head = EnsurePolygon(visualRoot, "Head", new Vector2(0f, -8f), new Color("f4d8bf"), [-8f, -6f, 8f, -6f, 8f, 6f, -8f, 6f]);
        _accent = EnsurePolygon(visualRoot, "Accent", new Vector2(0f, 3f), Colors.White, [-9f, -5f, 9f, -5f, 9f, 5f, -9f, 5f]);
        _facingIndicator = EnsurePolygon(visualRoot, "FacingIndicator", new Vector2(0f, -1f), new Color("2d241c"), [0f, -8f, 5f, 0f, -5f, 0f]);

        _sprite = visualRoot.GetNodeOrNull<Sprite2D>("Sprite2D") ?? new Sprite2D { Name = "Sprite2D", Centered = true, Visible = false };
        if (_sprite.GetParent() is null)
            visualRoot.AddChild(_sprite);

        _animatedSprite = visualRoot.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D") ?? new AnimatedSprite2D { Name = "AnimatedSprite2D", Centered = true, Visible = false };
        if (_animatedSprite.GetParent() is null)
            visualRoot.AddChild(_animatedSprite);
    }

    public TinyTownVisualStatus Status => _status;

    public void Configure(TinyTownArtProfile profile, TinyTownSpriteCatalog catalog)
    {
        _profile = profile ?? new TinyTownArtProfile();
        _catalog = catalog;
        _loadedSpriteKey = string.Empty;
        _loadedFramesKey = string.Empty;
        _status = new(_profile.EffectiveVillagerMode, TinyTownVisualMode.FallbackShapes, true, false);
    }

    public void Apply(TinyTownVillagerPresentation presentation)
    {
        var accentColor = ActivityColor(presentation.Activity);
        var facingRotation = FacingRotation(presentation.FacingDirection);
        var useAnimated = _profile.EffectiveVillagerMode == TinyTownVisualMode.AnimatedSprites;
        var useStatic = _profile.EffectiveVillagerMode == TinyTownVisualMode.StaticSprites;

        if (useAnimated && TryUseAnimatedSprites(presentation, accentColor, facingRotation))
            return;

        if (useStatic && TryUseStaticSprite(presentation, accentColor, facingRotation))
            return;

        ApplyFallback(presentation, accentColor, facingRotation);
    }

    private bool TryUseAnimatedSprites(TinyTownVillagerPresentation presentation, Color accentColor, float facingRotation)
    {
        if (_catalog is null)
            return false;

        var key = $"{presentation.Name}|{presentation.Personality}";
        if (_loadedFramesKey != key)
        {
            if (!_catalog.TryLoadVillagerFrames(_profile, presentation, out var frames) || frames is null)
                return false;

            _animatedSprite.SpriteFrames = frames;
            _loadedFramesKey = key;
        }

        SetFallbackVisible(false);
        _sprite.Visible = false;
        _animatedSprite.Visible = true;
        _animatedSprite.Modulate = accentColor.Lightened(0.12f);
        _animatedSprite.Rotation = 0f;
        PlayDirectionalAnimation(presentation.FacingDirection, presentation.Speed);
        _status = new(_profile.EffectiveVillagerMode, TinyTownVisualMode.AnimatedSprites, false, true);
        return true;
    }

    private bool TryUseStaticSprite(TinyTownVillagerPresentation presentation, Color accentColor, float facingRotation)
    {
        if (_catalog is null)
            return false;

        var key = $"{presentation.Name}|{presentation.Personality}";
        if (_loadedSpriteKey != key)
        {
            if (!_catalog.TryLoadVillagerTexture(_profile, presentation, out var texture) || texture is null)
                return false;

            _sprite.Texture = texture;
            _loadedSpriteKey = key;
        }

        SetFallbackVisible(false);
        _animatedSprite.Visible = false;
        _sprite.Visible = true;
        _sprite.Modulate = accentColor.Lightened(0.18f);
        _sprite.FlipH = presentation.FacingDirection == TinyTownFacingDirection.Left;
        _sprite.Rotation = presentation.FacingDirection is TinyTownFacingDirection.Up or TinyTownFacingDirection.Down
            ? 0f
            : 0f;
        _status = new(_profile.EffectiveVillagerMode, TinyTownVisualMode.StaticSprites, false, true);
        return true;
    }

    private void ApplyFallback(TinyTownVillagerPresentation presentation, Color accentColor, float facingRotation)
    {
        SetFallbackVisible(true);
        _sprite.Visible = false;
        _animatedSprite.Visible = false;
        _body.Color = accentColor;
        _head.Color = HeadColorFor(presentation.Personality);
        _accent.Color = accentColor.Darkened(0.18f);
        _facingIndicator.Color = presentation.Speed > 2f ? new Color("2d241c") : new Color(0.18f, 0.14f, 0.10f, 0.65f);
        _facingIndicator.Rotation = facingRotation;
        _body.Scale = new Vector2(1f + MathF.Min(presentation.Speed / 200f, 0.06f), 1f);
        _status = new(_profile.EffectiveVillagerMode, TinyTownVisualMode.FallbackShapes, true, false);
    }

    private void SetFallbackVisible(bool visible)
    {
        if (_shadow is not null)
            _shadow.Visible = visible;

        _body.Visible = visible;
        _head.Visible = visible;
        _accent.Visible = visible;
        _facingIndicator.Visible = visible;
    }

    private void PlayDirectionalAnimation(TinyTownFacingDirection facing, float speed)
    {
        if (_animatedSprite.SpriteFrames is null)
            return;

        var preferred = facing switch
        {
            TinyTownFacingDirection.Up => "walk_up",
            TinyTownFacingDirection.Left => "walk_left",
            TinyTownFacingDirection.Right => "walk_right",
            _ => "walk_down"
        };

        var idleFallback = facing switch
        {
            TinyTownFacingDirection.Up => "idle_up",
            TinyTownFacingDirection.Left => "idle_left",
            TinyTownFacingDirection.Right => "idle_right",
            _ => "idle_down"
        };

        var animationName = speed > 4f ? preferred : idleFallback;
        if (!_animatedSprite.SpriteFrames.HasAnimation(animationName))
        {
            animationName = _animatedSprite.SpriteFrames.GetAnimationNames().FirstOrDefault() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(animationName))
            return;

        if (_animatedSprite.Animation != animationName)
            _animatedSprite.Play(animationName);
        else if (!_animatedSprite.IsPlaying())
            _animatedSprite.Play();

        _animatedSprite.SpeedScale = speed > 4f ? 1f : 0.4f;
    }

    private static Polygon2D CreateBody(Node parent)
    {
        var body = new Polygon2D
        {
            Name = "Body",
            Color = new Color("7b8ea2"),
            Polygon = [new Vector2(-11f, -10f), new Vector2(11f, -10f), new Vector2(11f, 10f), new Vector2(-11f, 10f)]
        };
        parent.AddChild(body);
        return body;
    }

    private static Polygon2D EnsurePolygon(Node parent, string name, Vector2 position, Color color, float[] points)
    {
        if (parent.GetNodeOrNull<Polygon2D>(name) is { } existing)
            return existing;

        var polygon = new Polygon2D
        {
            Name = name,
            Position = position,
            Color = color,
            Polygon = CreatePoints(points)
        };
        parent.AddChild(polygon);
        return polygon;
    }

    private static Vector2[] CreatePoints(float[] values)
    {
        var points = new Vector2[values.Length / 2];
        for (var i = 0; i < points.Length; i++)
            points[i] = new Vector2(values[i * 2], values[(i * 2) + 1]);

        return points;
    }

    private static float FacingRotation(TinyTownFacingDirection facing)
    {
        return facing switch
        {
            TinyTownFacingDirection.Up => Mathf.Pi,
            TinyTownFacingDirection.Left => -Mathf.Pi * 0.5f,
            TinyTownFacingDirection.Right => Mathf.Pi * 0.5f,
            _ => 0f
        };
    }

    private static Color ActivityColor(string activity) => activity switch
    {
        "GoToWell" => new Color("5f9ecf"),
        "DrinkAtWell" => new Color("2e6f95"),
        "GoToMarket" => new Color("eea04f"),
        "ShopAtMarket" => new Color("cb6d2e"),
        "RestAtHome" => new Color("8a6c52"),
        "ReturnHome" => new Color("9f8264"),
        "TendGarden" => new Color("4f8b57"),
        "Wander" => new Color("6c8da6"),
        "Socialize" => new Color("b45f7b"),
        "Idle / Think" => new Color("7b8ea2"),
        _ => new Color("7f8f70")
    };

    private static Color HeadColorFor(string personality) => personality switch
    {
        "Social shopper" => new Color("f3d5be"),
        "Restless wanderer" => new Color("eac4a0"),
        "Quiet gardener" => new Color("f0cfb3"),
        "Cozy homebody" => new Color("e4c3ab"),
        _ => new Color("efd4be")
    };
}
