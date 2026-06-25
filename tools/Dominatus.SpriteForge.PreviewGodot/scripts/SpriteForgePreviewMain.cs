using Dominatus.Assets.Toml;
using Dominatus.SpriteForge;
using Godot;
using System.Text.Json;

namespace Dominatus.SpriteForge.PreviewGodot;

public partial class SpriteForgePreviewMain : Control
{
    private const int Padding = 24;
    private const int RightPanelWidth = 420;
    private const int BottomPanelHeight = 220;

    private PreviewConfig? _config;
    private SpriteForgeAtlas? _atlas;
    private Texture2D? _atlasTexture;
    private IReadOnlyList<SpriteForgeResolvedFrame> _selectedFrames = [];
    private string? _selectedSpriteId;
    private string? _selectedAnimationId;
    private string _status = "Loading SpriteForge preview...";
    private string _artifactDirectory = string.Empty;
    private string _previewPath = string.Empty;
    private string _debugJsonPath = string.Empty;
    private int _animatedFrameIndex;
    private double _frameAccumulator;
    private bool _captureScheduled;

    public override void _Ready()
    {
        _config = PreviewConfig.Parse(OS.GetCmdlineUserArgs());
        _artifactDirectory = Path.GetFullPath(_config.ArtifactsDirectory);
        _previewPath = Path.Combine(_artifactDirectory, "preview.png");
        _debugJsonPath = Path.Combine(_artifactDirectory, "preview-debug.json");
        Directory.CreateDirectory(_artifactDirectory);

        var loadResult = SpriteForgeTomlLoader.LoadFile(_config.TomlPath, new SpriteForgeLoadOptions { RequireImageFileExists = true });
        if (!loadResult.Success || loadResult.Atlas is null)
        {
            _status = "SpriteForge TOML load failed.";
            WriteDebugSummary(loadResult, overlayFrames: []);
            GD.PushError($"{_status}{System.Environment.NewLine}{AssetDiagnosticFormatter.FormatMany(loadResult.Diagnostics)}");
            GetTree().Quit(2);
            return;
        }

        _atlas = loadResult.Atlas;
        _atlasTexture = LoadAtlasTexture(_atlas.ResolvedImagePath);
        ResolveSelection();
        _status = BuildStatus();
        ConfigureWindow();
        QueueRedraw();
        _captureScheduled = true;
    }

    public override void _Process(double delta)
    {
        if (_selectedFrames.Count > 1)
        {
            var fps = GetSelectedAnimationFps();
            if (fps > 0f)
            {
                _frameAccumulator += delta;
                var secondsPerFrame = 1.0 / fps;
                if (_frameAccumulator >= secondsPerFrame)
                {
                    _frameAccumulator -= secondsPerFrame;
                    _animatedFrameIndex = (_animatedFrameIndex + 1) % _selectedFrames.Count;
                    QueueRedraw();
                }
            }
        }

        if (!_captureScheduled)
            return;

        _captureScheduled = false;
        _ = CaptureArtifactsAsync();
    }

    public override void _Draw()
    {
        DrawBackground();

        if (_atlas is null || _atlasTexture is null)
        {
            DrawString(GetThemeDefaultFont(), new Vector2(Padding, Padding + 24), _status, HorizontalAlignment.Left, -1, 20, Colors.White);
            return;
        }

        var atlasOrigin = new Vector2(Padding, Padding);
        var atlasRect = new Rect2(atlasOrigin, new Vector2(_atlas.Width, _atlas.Height));

        DrawTexture(_atlasTexture, atlasOrigin);
        DrawRect(atlasRect, Colors.White, false, 3f);
        DrawGridOverlays(atlasOrigin);
        DrawResolvedFrameOverlays(atlasOrigin);
        DrawSelectedPreview(atlasRect);
        DrawStatusText(atlasRect);
    }

    private async Task CaptureArtifactsAsync()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var image = GetViewport().GetTexture().GetImage();
        image.SavePng(_previewPath);
        WriteDebugSummary(
            new SpriteForgeLoadResult
            {
                Atlas = _atlas,
                Diagnostics = []
            },
            BuildOverlayFrames());

        if (_config?.SmokeMode == true)
            GetTree().Quit();
    }

    private void DrawBackground()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("11161d"));
    }

    private void DrawGridOverlays(Vector2 atlasOrigin)
    {
        if (_atlas is null)
            return;

        var gridOutline = new Color("4dd2ff");
        var cellLine = new Color(0.35f, 0.76f, 0.92f, 0.7f);
        var font = GetThemeDefaultFont();

        foreach (var grid in _atlas.Grids.Values.OrderBy(g => g.Id, StringComparer.Ordinal))
        {
            var width = (grid.Columns * grid.CellWidth) + (Math.Max(grid.Columns - 1, 0) * grid.GapX);
            var height = (grid.Rows * grid.CellHeight) + (Math.Max(grid.Rows - 1, 0) * grid.GapY);
            var gridRect = new Rect2(atlasOrigin + new Vector2(grid.OriginX, grid.OriginY), new Vector2(width, height));
            DrawRect(gridRect, gridOutline, false, 2f);
            DrawString(font, gridRect.Position + new Vector2(6, 18), grid.Id, HorizontalAlignment.Left, -1, 14, Colors.WhiteSmoke);

            for (var col = 1; col < grid.Columns; col++)
            {
                var x = gridRect.Position.X + (col * grid.CellWidth) + ((col - 1) * grid.GapX);
                DrawLine(new Vector2(x, gridRect.Position.Y), new Vector2(x, gridRect.End.Y), cellLine, 1f);
            }

            for (var row = 1; row < grid.Rows; row++)
            {
                var y = gridRect.Position.Y + (row * grid.CellHeight) + ((row - 1) * grid.GapY);
                DrawLine(new Vector2(gridRect.Position.X, y), new Vector2(gridRect.End.X, y), cellLine, 1f);
            }
        }
    }

    private void DrawResolvedFrameOverlays(Vector2 atlasOrigin)
    {
        foreach (var frame in BuildOverlayFrames())
        {
            var isSelected = _selectedFrames.Any(selected =>
                selected.SpriteId == frame.SpriteId
                && selected.AnimationId == frame.AnimationId
                && selected.FrameIndex == frame.FrameIndex);
            var color = isSelected
                ? new Color("ffd166")
                : new Color(0.93f, 0.45f, 0.36f, 0.7f);
            var rect = new Rect2(
                atlasOrigin + new Vector2(frame.X, frame.Y),
                new Vector2(frame.Width, frame.Height));
            DrawRect(rect, color, false, isSelected ? 3f : 2f);
            DrawPivotMarker(atlasOrigin + new Vector2(frame.PivotX, frame.PivotY), color);
        }
    }

    private void DrawSelectedPreview(Rect2 atlasRect)
    {
        if (_atlasTexture is null || _selectedFrames.Count == 0)
            return;

        var panelPosition = new Vector2(atlasRect.End.X + Padding, Padding);
        var panelRect = new Rect2(panelPosition, new Vector2(RightPanelWidth, BottomPanelHeight));
        DrawRect(panelRect, new Color("1c2430"), true);
        DrawRect(panelRect, new Color("6f8093"), false, 2f);

        var font = GetThemeDefaultFont();
        var title = _selectedAnimationId is null ? _selectedSpriteId ?? "selection" : $"{_selectedSpriteId}.{_selectedAnimationId}";
        DrawString(font, panelPosition + new Vector2(12, 24), title, HorizontalAlignment.Left, -1, 18, Colors.White);

        var frame = _selectedFrames[Math.Clamp(_animatedFrameIndex, 0, _selectedFrames.Count - 1)];
        var sourceRect = new Rect2(frame.X, frame.Y, frame.Width, frame.Height);
        var scale = MathF.Min(
            (panelRect.Size.X - 48f) / Math.Max(frame.Width, 1),
            (panelRect.Size.Y - 72f) / Math.Max(frame.Height, 1));
        scale = MathF.Max(scale, 1f);
        var drawSize = new Vector2(frame.Width * scale, frame.Height * scale);
        var drawPosition = panelPosition + new Vector2(
            (panelRect.Size.X - drawSize.X) / 2f,
            44f + ((panelRect.Size.Y - 56f - drawSize.Y) / 2f));
        DrawTextureRectRegion(_atlasTexture, new Rect2(drawPosition, drawSize), sourceRect);
        DrawRect(new Rect2(drawPosition, drawSize), new Color("ffd166"), false, 2f);

        var pivotPosition = new Vector2(
            drawPosition.X + ((frame.PivotX - frame.X) * scale),
            drawPosition.Y + ((frame.PivotY - frame.Y) * scale));
        DrawPivotMarker(pivotPosition, new Color("ff5d73"));
        DrawString(
            font,
            panelPosition + new Vector2(12, panelRect.Size.Y - 14),
            $"frame {frame.FrameIndex}  source={frame.Source}  offset=({frame.OffsetX},{frame.OffsetY})  scale={frame.Scale:0.##}",
            HorizontalAlignment.Left,
            -1,
            13,
            new Color("d8dee8"));
    }

    private void DrawStatusText(Rect2 atlasRect)
    {
        var font = GetThemeDefaultFont();
        var y = atlasRect.End.Y + Padding + 20;
        DrawString(font, new Vector2(Padding, y), _status, HorizontalAlignment.Left, -1, 16, Colors.WhiteSmoke);
        DrawString(font, new Vector2(Padding, y + 22), $"Artifacts: {_previewPath}", HorizontalAlignment.Left, -1, 12, new Color("a6b3c2"));
        DrawString(font, new Vector2(Padding, y + 40), $"Debug JSON: {_debugJsonPath}", HorizontalAlignment.Left, -1, 12, new Color("a6b3c2"));
    }

    private void DrawPivotMarker(Vector2 position, Color color)
    {
        DrawLine(position + new Vector2(-6, 0), position + new Vector2(6, 0), color, 2f);
        DrawLine(position + new Vector2(0, -6), position + new Vector2(0, 6), color, 2f);
        DrawCircle(position, 2.5f, color);
    }

    private void ResolveSelection()
    {
        if (_atlas is null)
            return;

        if (!TryParseSelection(_config?.Selected, out var spriteId, out var animationId))
        {
            var firstSprite = _atlas.Sprites.Values.OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
            spriteId = firstSprite?.Id;
            animationId = firstSprite?.Animations.OrderBy(a => a.Key, StringComparer.Ordinal).FirstOrDefault().Key;
        }

        _selectedSpriteId = spriteId;
        _selectedAnimationId = animationId;
        if (string.IsNullOrWhiteSpace(spriteId) || !_atlas.Sprites.ContainsKey(spriteId))
        {
            _selectedFrames = [];
            return;
        }

        var resolver = new SpriteForgeResolver();
        _selectedFrames = string.IsNullOrWhiteSpace(animationId)
            ? [resolver.ResolveStaticSprite(_atlas, spriteId)]
            : resolver.ResolveAnimation(_atlas, spriteId, animationId);
    }

    private IReadOnlyList<SpriteForgeResolvedFrame> BuildOverlayFrames()
    {
        if (_atlas is null)
            return [];

        var resolver = new SpriteForgeResolver();
        var frames = new List<SpriteForgeResolvedFrame>();
        foreach (var sprite in _atlas.Sprites.Values.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            if (sprite.Animations.Count > 0)
            {
                var animation = sprite.Animations.Values.OrderBy(a => a.Id, StringComparer.Ordinal).First();
                try
                {
                    frames.AddRange(resolver.ResolveAnimation(_atlas, sprite.Id, animation.Id).Take(1));
                }
                catch
                {
                    // Keep preview resilient even if one sprite cannot resolve.
                }
            }
            else if (!string.IsNullOrWhiteSpace(sprite.Frame) || (!string.IsNullOrWhiteSpace(sprite.Grid) && sprite.Row.HasValue && sprite.Col.HasValue))
            {
                try
                {
                    frames.Add(resolver.ResolveStaticSprite(_atlas, sprite.Id));
                }
                catch
                {
                    // Keep preview resilient even if one sprite cannot resolve.
                }
            }
        }

        return frames;
    }

    private void ConfigureWindow()
    {
        if (_atlas is null)
            return;

        var width = _atlas.Width + RightPanelWidth + (Padding * 3);
        var height = Math.Max(_atlas.Height + BottomPanelHeight + (Padding * 3), 840);
        CustomMinimumSize = new Vector2(width, height);
        if (GetWindow() is Window window)
        {
            window.Size = new Vector2I(width, height);
            window.Title = "Dominatus.SpriteForge Preview";
        }
    }

    private string BuildStatus()
    {
        if (_atlas is null)
            return _status;

        return $"Atlas {_atlas.Image} ({_atlas.Width}x{_atlas.Height})  grids={_atlas.Grids.Count}  sprites={_atlas.Sprites.Count}  frames={_atlas.Frames.Count}";
    }

    private float GetSelectedAnimationFps()
    {
        if (_atlas is null || string.IsNullOrWhiteSpace(_selectedSpriteId) || string.IsNullOrWhiteSpace(_selectedAnimationId))
            return 0f;

        return _atlas.Sprites.TryGetValue(_selectedSpriteId, out var sprite)
            && sprite.Animations.TryGetValue(_selectedAnimationId, out var animation)
            ? animation.Fps
            : 0f;
    }

    private void WriteDebugSummary(SpriteForgeLoadResult loadResult, IReadOnlyList<SpriteForgeResolvedFrame> overlayFrames)
    {
        var payload = new
        {
            success = loadResult.Success,
            tomlPath = _config?.TomlPath,
            selected = _config?.Selected,
            diagnostics = loadResult.Diagnostics.Select(d => new
            {
                severity = d.Severity.ToString(),
                d.Code,
                d.Message,
                d.SourcePath,
                d.KeyPath,
                d.Line,
                d.Column
            }),
            atlas = _atlas is null ? null : new
            {
                _atlas.Image,
                _atlas.Width,
                _atlas.Height,
                grids = _atlas.Grids.Values.Select(g => new
                {
                    g.Id,
                    g.OriginX,
                    g.OriginY,
                    g.Columns,
                    g.Rows,
                    g.CellWidth,
                    g.CellHeight,
                    g.GapX,
                    g.GapY,
                    g.DefaultPivot
                }),
                sprites = _atlas.Sprites.Values.Select(s => new
                {
                    s.Id,
                    s.Kind,
                    s.DisplayName,
                    s.Grid,
                    s.Row,
                    s.Col,
                    s.Frame,
                    s.Scale,
                    s.OffsetX,
                    s.OffsetY,
                    s.Pivot,
                    animations = s.Animations.Values.Select(a => new
                    {
                        a.Id,
                        a.Grid,
                        a.Row,
                        a.Fps,
                        a.Loop,
                        frameCount = a.Frames.Count
                    })
                }),
                frames = _atlas.Frames.Values.Select(f => new
                {
                    f.Id,
                    f.X,
                    f.Y,
                    f.Width,
                    f.Height,
                    f.Pivot,
                    f.OffsetX,
                    f.OffsetY,
                    f.Scale
                })
            },
            overlayFrames = overlayFrames.Select(f => new
            {
                f.SpriteId,
                f.AnimationId,
                f.FrameIndex,
                f.X,
                f.Y,
                f.Width,
                f.Height,
                f.OffsetX,
                f.OffsetY,
                f.Scale,
                f.Pivot,
                f.PivotX,
                f.PivotY,
                source = f.Source.ToString(),
                f.GridId,
                f.Row,
                f.Col,
                f.FrameId
            })
        };

        File.WriteAllText(_debugJsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static Texture2D LoadAtlasTexture(string path)
    {
        var image = Image.LoadFromFile(path);
        return ImageTexture.CreateFromImage(image);
    }

    private static bool TryParseSelection(string? selected, out string? spriteId, out string? animationId)
    {
        spriteId = null;
        animationId = null;
        if (string.IsNullOrWhiteSpace(selected))
            return false;

        var trimmed = selected.Trim();
        var lastDot = trimmed.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == trimmed.Length - 1)
        {
            spriteId = trimmed;
            return true;
        }

        spriteId = trimmed[..lastDot];
        animationId = trimmed[(lastDot + 1)..];
        return true;
    }

    private sealed record PreviewConfig(
        string TomlPath,
        string ArtifactsDirectory,
        string? Selected,
        bool SmokeMode)
    {
        public static PreviewConfig Parse(string[] args)
        {
            string? tomlPath = System.Environment.GetEnvironmentVariable("DOMINATUS_SPRITEFORGE_TOML");
            var artifactsDirectory = System.Environment.GetEnvironmentVariable("DOMINATUS_SPRITEFORGE_ARTIFACTS");
            string? selected = System.Environment.GetEnvironmentVariable("DOMINATUS_SPRITEFORGE_SELECTED");
            var smokeMode = ParseBool(System.Environment.GetEnvironmentVariable("DOMINATUS_SPRITEFORGE_SMOKE"), defaultValue: true);

            foreach (var arg in args)
            {
                if (arg.StartsWith("--spriteforge-toml=", StringComparison.OrdinalIgnoreCase))
                {
                    tomlPath = arg["--spriteforge-toml=".Length..];
                }
                else if (arg.StartsWith("--spriteforge-artifacts=", StringComparison.OrdinalIgnoreCase))
                {
                    artifactsDirectory = arg["--spriteforge-artifacts=".Length..];
                }
                else if (arg.StartsWith("--spriteforge-selected=", StringComparison.OrdinalIgnoreCase))
                {
                    selected = arg["--spriteforge-selected=".Length..];
                }
                else if (arg.StartsWith("--spriteforge-smoke=", StringComparison.OrdinalIgnoreCase))
                {
                    smokeMode = ParseBool(arg["--spriteforge-smoke=".Length..], defaultValue: smokeMode);
                }
            }

            if (string.IsNullOrWhiteSpace(tomlPath))
                throw new InvalidOperationException("Provide DOMINATUS_SPRITEFORGE_TOML or --spriteforge-toml=...");

            return new PreviewConfig(
                Path.GetFullPath(tomlPath),
                string.IsNullOrWhiteSpace(artifactsDirectory)
                    ? Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "spriteforge")
                    : Path.GetFullPath(artifactsDirectory),
                string.IsNullOrWhiteSpace(selected) ? null : selected.Trim(),
                smokeMode);
        }

        private static bool ParseBool(string? value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            return value.Trim().ToLowerInvariant() switch
            {
                "1" or "true" or "yes" => true,
                "0" or "false" or "no" => false,
                _ => defaultValue
            };
        }
    }
}
