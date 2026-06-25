using Dominatus.Assets.Toml;
using System.Buffers.Binary;
using System.Collections;

namespace Dominatus.SpriteForge;

public static class SpriteForgeTomlLoader
{
    public static SpriteForgeLoadResult LoadFile(string path, SpriteForgeLoadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedOptions = options ?? new SpriteForgeLoadOptions();
        var loadResult = TomlAssetLoader.LoadFile<SpriteForgeAtlasTomlDocument>(
            path,
            new SpriteForgeTomlValidator(normalizedOptions));

        if (loadResult.Value is null)
        {
            return new SpriteForgeLoadResult
            {
                Atlas = null,
                Diagnostics = loadResult.Diagnostics
            };
        }

        var diagnostics = new List<AssetDiagnostic>(loadResult.Diagnostics);
        try
        {
            var atlas = BuildAtlas(path, loadResult.Value, diagnostics, loadResult.SourceMap);
            return new SpriteForgeLoadResult
            {
                Atlas = diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error) ? null : atlas,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            diagnostics.Add(AssetValidation.Error("spriteforge.transform", ex.Message, path));
            return new SpriteForgeLoadResult
            {
                Atlas = null,
                Diagnostics = diagnostics
            };
        }
    }

    internal static string ResolveImagePath(string sourcePath, string imagePath)
    {
        var trimmed = imagePath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("SpriteForge atlas image path is required.");

        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed);

        var directory = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(directory, trimmed));
    }

    private static SpriteForgeAtlas BuildAtlas(
        string sourcePath,
        SpriteForgeAtlasTomlDocument document,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap)
    {
        var atlas = document.Atlas ?? throw new InvalidOperationException("SpriteForge TOML is missing the [atlas] table.");
        var grids = BuildGrids(document, sourcePath, diagnostics, sourceMap);
        var frames = BuildFrames(document, sourcePath, diagnostics, sourceMap);
        var sprites = BuildSprites(document, sourcePath, diagnostics, sourceMap);

        return new SpriteForgeAtlas
        {
            SourcePath = sourcePath,
            Image = atlas.Image.Trim(),
            ResolvedImagePath = ResolveImagePath(sourcePath, atlas.Image),
            Width = atlas.Width,
            Height = atlas.Height,
            Grids = grids,
            Sprites = sprites,
            Frames = frames
        };
    }

    private static IReadOnlyDictionary<string, SpriteForgeGrid> BuildGrids(
        SpriteForgeAtlasTomlDocument document,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap)
    {
        var grids = new Dictionary<string, SpriteForgeGrid>(StringComparer.Ordinal);
        foreach (var (gridId, gridDoc) in document.Grids)
        {
            if (!IsValidId(gridId))
            {
                diagnostics.Add(CreateError("spriteforge.invalid_grid_id", $"Grid id '{gridId}' is invalid. Use letters, numbers, '.', '_' or '-'.", sourcePath, $"grids.{QuoteKey(gridId)}", sourceMap));
                continue;
            }

            grids[gridId] = new SpriteForgeGrid
            {
                Id = gridId,
                OriginX = gridDoc.OriginX,
                OriginY = gridDoc.OriginY,
                Columns = gridDoc.Columns,
                Rows = gridDoc.Rows,
                CellWidth = gridDoc.CellWidth,
                CellHeight = gridDoc.CellHeight,
                DefaultPivot = NormalizePivotOrNull(gridDoc.DefaultPivot, $"grids.{QuoteKey(gridId)}.default_pivot", sourcePath, diagnostics, sourceMap),
                GapX = gridDoc.GapX ?? 0,
                GapY = gridDoc.GapY ?? 0
            };
        }

        return grids;
    }

    private static IReadOnlyDictionary<string, SpriteForgeFrame> BuildFrames(
        SpriteForgeAtlasTomlDocument document,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap)
    {
        var frames = new Dictionary<string, SpriteForgeFrame>(StringComparer.Ordinal);
        foreach (var (frameId, frameDoc) in document.Frames)
        {
            if (!IsValidId(frameId))
            {
                diagnostics.Add(CreateError("spriteforge.invalid_frame_id", $"Frame id '{frameId}' is invalid. Use letters, numbers, '.', '_' or '-'.", sourcePath, $"frames.{QuoteKey(frameId)}", sourceMap));
                continue;
            }

            frames[frameId] = new SpriteForgeFrame
            {
                Id = frameId,
                X = frameDoc.X,
                Y = frameDoc.Y,
                Width = frameDoc.Width,
                Height = frameDoc.Height,
                Pivot = NormalizePivotOrNull(frameDoc.Pivot, $"frames.{QuoteKey(frameId)}.pivot", sourcePath, diagnostics, sourceMap),
                OffsetX = frameDoc.OffsetX ?? 0,
                OffsetY = frameDoc.OffsetY ?? 0,
                Scale = frameDoc.Scale ?? 1f
            };
        }

        return frames;
    }

    private static IReadOnlyDictionary<string, SpriteForgeSprite> BuildSprites(
        SpriteForgeAtlasTomlDocument document,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap)
    {
        var sprites = new Dictionary<string, SpriteForgeSprite>(StringComparer.Ordinal);
        foreach (var (spriteId, spriteDoc) in document.Sprites)
        {
            if (!IsValidId(spriteId))
            {
                diagnostics.Add(CreateError("spriteforge.invalid_sprite_id", $"Sprite id '{spriteId}' is invalid. Use letters, numbers, '.', '_' or '-'.", sourcePath, $"sprites.{QuoteKey(spriteId)}", sourceMap));
                continue;
            }

            var animations = new Dictionary<string, SpriteForgeAnimation>(StringComparer.Ordinal);
            foreach (var (animationId, animationDoc) in spriteDoc.Animations)
            {
                if (!IsValidId(animationId))
                {
                    diagnostics.Add(CreateError("spriteforge.invalid_animation_id", $"Animation id '{animationId}' is invalid. Use letters, numbers, '.', '_' or '-'.", sourcePath, $"sprites.{QuoteKey(spriteId)}.animations.{QuoteKey(animationId)}", sourceMap));
                    continue;
                }

                var frames = BuildAnimationFrameRefs(spriteId, animationId, animationDoc, sourcePath, diagnostics, sourceMap);
                animations[animationId] = new SpriteForgeAnimation
                {
                    Id = animationId,
                    Grid = NullIfWhiteSpace(animationDoc.Grid),
                    Row = animationDoc.Row,
                    Frames = frames,
                    Fps = animationDoc.Fps ?? 0f,
                    Loop = animationDoc.Loop ?? true
                };
            }

            sprites[spriteId] = new SpriteForgeSprite
            {
                Id = spriteId,
                Kind = spriteDoc.Kind?.Trim() ?? string.Empty,
                DisplayName = NullIfWhiteSpace(spriteDoc.DisplayName),
                Grid = NullIfWhiteSpace(spriteDoc.Grid),
                Row = spriteDoc.Row,
                Col = spriteDoc.Col,
                Frame = NullIfWhiteSpace(spriteDoc.Frame),
                Scale = spriteDoc.Scale ?? 1f,
                OffsetX = spriteDoc.OffsetX ?? 0,
                OffsetY = spriteDoc.OffsetY ?? 0,
                Pivot = NormalizePivotOrNull(spriteDoc.Pivot, $"sprites.{QuoteKey(spriteId)}.pivot", sourcePath, diagnostics, sourceMap),
                Animations = animations
            };
        }

        return sprites;
    }

    private static IReadOnlyList<SpriteForgeFrameRef> BuildAnimationFrameRefs(
        string spriteId,
        string animationId,
        SpriteForgeAnimationTomlDocument animationDoc,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap)
    {
        var result = new List<SpriteForgeFrameRef>(animationDoc.Frames.Count);
        for (var i = 0; i < animationDoc.Frames.Count; i++)
        {
            var keyPath = $"sprites.{QuoteKey(spriteId)}.animations.{QuoteKey(animationId)}.frames[{i}]";
            if (!TryConvertFrameRef(animationDoc.Frames[i], keyPath, sourcePath, diagnostics, sourceMap, out var frameRef))
                continue;

            result.Add(frameRef!);
        }

        return result;
    }

    private static bool TryConvertFrameRef(
        object? rawValue,
        string keyPath,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap,
        out SpriteForgeFrameRef? frameRef)
    {
        switch (rawValue)
        {
            case int col:
                frameRef = new SpriteForgeFrameRef { Col = col };
                return true;
            case long longCol when longCol is >= int.MinValue and <= int.MaxValue:
                frameRef = new SpriteForgeFrameRef { Col = (int)longCol };
                return true;
            case string frameId when !string.IsNullOrWhiteSpace(frameId):
                frameRef = new SpriteForgeFrameRef { Frame = frameId.Trim() };
                return true;
            case IDictionary<string, object> dictionary:
                return TryConvertFrameRefDictionary(dictionary, keyPath, sourcePath, diagnostics, sourceMap, out frameRef);
            case IDictionary nonGenericDictionary:
                return TryConvertFrameRefDictionary(
                    nonGenericDictionary.Keys.Cast<object>().ToDictionary(
                        key => key.ToString() ?? string.Empty,
                        key => nonGenericDictionary[key]!),
                    keyPath,
                    sourcePath,
                    diagnostics,
                    sourceMap,
                    out frameRef);
            default:
                diagnostics.Add(CreateError(
                    "spriteforge.invalid_frame_ref",
                    "Frame references must be an integer grid column, a frame id string, or a table with grid/row/col or frame.",
                    sourcePath,
                    keyPath,
                    sourceMap));
                frameRef = null;
                return false;
        }
    }

    private static bool TryConvertFrameRefDictionary(
        IDictionary<string, object> dictionary,
        string keyPath,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap,
        out SpriteForgeFrameRef? frameRef)
    {
        var frame = TryReadString(dictionary, "frame");
        var grid = TryReadString(dictionary, "grid");
        var row = TryReadInt(dictionary, "row");
        var col = TryReadInt(dictionary, "col") ?? TryReadInt(dictionary, "index");

        if (string.IsNullOrWhiteSpace(frame) && string.IsNullOrWhiteSpace(grid) && !row.HasValue && !col.HasValue)
        {
            diagnostics.Add(CreateError(
                "spriteforge.empty_frame_ref",
                "Frame reference tables must include frame or grid/row/col data.",
                sourcePath,
                keyPath,
                sourceMap));
            frameRef = null;
            return false;
        }

        frameRef = new SpriteForgeFrameRef
        {
            Frame = NullIfWhiteSpace(frame),
            Grid = NullIfWhiteSpace(grid),
            Row = row,
            Col = col
        };

        return true;
    }

    private static bool IsValidId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-')
                continue;

            return false;
        }

        return true;
    }

    private static string? NormalizePivotOrNull(
        string? pivot,
        string keyPath,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap)
    {
        if (string.IsNullOrWhiteSpace(pivot))
            return null;

        var normalized = SpriteForgePivots.Normalize(pivot);
        if (SpriteForgePivots.IsSupported(normalized))
            return normalized;

        diagnostics.Add(CreateError(
            "spriteforge.invalid_pivot",
            $"Unsupported pivot '{pivot}'. Expected {string.Join(", ", SpriteForgePivots.Supported)}.",
            sourcePath,
            keyPath,
            sourceMap));
        return null;
    }

    private static string QuoteKey(string key) => $"\"{key}\"";

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? TryReadInt(IDictionary<string, object> values, string key)
    {
        if (!values.TryGetValue(key, out var raw) || raw is null)
            return null;

        return raw switch
        {
            int value => value,
            long value when value is >= int.MinValue and <= int.MaxValue => (int)value,
            _ => null
        };
    }

    private static string? TryReadString(IDictionary<string, object> values, string key)
    {
        if (!values.TryGetValue(key, out var raw) || raw is null)
            return null;

        return raw as string;
    }

    private static AssetDiagnostic CreateError(
        string code,
        string message,
        string sourcePath,
        string keyPath,
        TomlAssetSourceMap? sourceMap) =>
        AssetValidation.Error(
            code,
            message,
            sourcePath,
            keyPath: keyPath,
            span: sourceMap?.TryGetSpan(keyPath, out var span) == true ? span : null);

    private sealed class SpriteForgeTomlValidator : IAssetValidator<SpriteForgeAtlasTomlDocument>
    {
        private readonly SpriteForgeLoadOptions _options;

        public SpriteForgeTomlValidator(SpriteForgeLoadOptions options)
        {
            _options = options;
        }

        public IReadOnlyList<AssetDiagnostic> Validate(SpriteForgeAtlasTomlDocument asset, AssetValidationContext context)
        {
            var diagnostics = new List<AssetDiagnostic>();
            var sourcePath = context.SourcePath ?? string.Empty;

            if (asset.Atlas is null)
            {
                diagnostics.Add(AssetValidation.Required("atlas", sourcePath, "atlas"));
                return diagnostics;
            }

            ValidateAtlas(asset.Atlas, sourcePath, diagnostics, context);
            ValidateGrids(asset, sourcePath, diagnostics, context);
            ValidateFrames(asset, sourcePath, diagnostics, context);
            ValidateSprites(asset, sourcePath, diagnostics, context);

            if (_options.RequireImageFileExists
                && !string.IsNullOrWhiteSpace(asset.Atlas.Image)
                && !diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error && d.KeyPath == "atlas.image"))
            {
                var resolvedImagePath = ResolveImagePath(sourcePath, asset.Atlas.Image);
                if (!File.Exists(resolvedImagePath))
                {
                    diagnostics.Add(AssetValidation.Error(
                        "spriteforge.image_missing",
                        $"SpriteForge image '{resolvedImagePath}' does not exist.",
                        sourcePath,
                        keyPath: "atlas.image",
                        span: context.GetSpan("atlas.image")));
                }
            }

            return diagnostics;
        }

        private static void ValidateAtlas(
            SpriteForgeAtlasSection atlas,
            string sourcePath,
            List<AssetDiagnostic> diagnostics,
            AssetValidationContext context)
        {
            if (string.IsNullOrWhiteSpace(atlas.Image))
                diagnostics.Add(AssetValidation.Required("atlas.image", sourcePath, "atlas.image"));

            RequirePositive(atlas.Width, "atlas.width", "spriteforge.width_invalid", "Atlas width must be greater than zero.", sourcePath, diagnostics, context);
            RequirePositive(atlas.Height, "atlas.height", "spriteforge.height_invalid", "Atlas height must be greater than zero.", sourcePath, diagnostics, context);
        }

        private static void ValidateGrids(
            SpriteForgeAtlasTomlDocument asset,
            string sourcePath,
            List<AssetDiagnostic> diagnostics,
            AssetValidationContext context)
        {
            foreach (var (gridId, grid) in asset.Grids)
            {
                var keyPath = $"grids.{QuoteKey(gridId)}";
                RequirePositive(grid.Columns, $"{keyPath}.columns", "spriteforge.grid_columns_invalid", $"Grid '{gridId}' columns must be greater than zero.", sourcePath, diagnostics, context);
                RequirePositive(grid.Rows, $"{keyPath}.rows", "spriteforge.grid_rows_invalid", $"Grid '{gridId}' rows must be greater than zero.", sourcePath, diagnostics, context);
                RequirePositive(grid.CellWidth, $"{keyPath}.cell_width", "spriteforge.grid_cell_width_invalid", $"Grid '{gridId}' cell_width must be greater than zero.", sourcePath, diagnostics, context);
                RequirePositive(grid.CellHeight, $"{keyPath}.cell_height", "spriteforge.grid_cell_height_invalid", $"Grid '{gridId}' cell_height must be greater than zero.", sourcePath, diagnostics, context);

                var width = ComputeGridPixelWidth(grid);
                var height = ComputeGridPixelHeight(grid);
                if (grid.OriginX < 0 || grid.OriginY < 0 || grid.OriginX + width > asset.Atlas!.Width || grid.OriginY + height > asset.Atlas.Height)
                {
                    diagnostics.Add(AssetValidation.Error(
                        "spriteforge.grid_out_of_bounds",
                        $"Grid '{gridId}' exceeds atlas bounds.",
                        sourcePath,
                        keyPath: keyPath,
                        span: context.GetSpan(keyPath)));
                }

                if (!string.IsNullOrWhiteSpace(grid.DefaultPivot) && !SpriteForgePivots.IsSupported(grid.DefaultPivot))
                {
                    diagnostics.Add(AssetValidation.Error(
                        "spriteforge.invalid_pivot",
                        $"Unsupported pivot '{grid.DefaultPivot}'. Expected {string.Join(", ", SpriteForgePivots.Supported)}.",
                        sourcePath,
                        keyPath: $"{keyPath}.default_pivot",
                        span: context.GetSpan($"{keyPath}.default_pivot")));
                }
            }
        }

        private static void ValidateFrames(
            SpriteForgeAtlasTomlDocument asset,
            string sourcePath,
            List<AssetDiagnostic> diagnostics,
            AssetValidationContext context)
        {
            foreach (var (frameId, frame) in asset.Frames)
            {
                var keyPath = $"frames.{QuoteKey(frameId)}";
                RequirePositive(frame.Width, $"{keyPath}.width", "spriteforge.frame_width_invalid", $"Frame '{frameId}' width must be greater than zero.", sourcePath, diagnostics, context);
                RequirePositive(frame.Height, $"{keyPath}.height", "spriteforge.frame_height_invalid", $"Frame '{frameId}' height must be greater than zero.", sourcePath, diagnostics, context);

                if (frame.X < 0 || frame.Y < 0 || frame.X + frame.Width > asset.Atlas!.Width || frame.Y + frame.Height > asset.Atlas.Height)
                {
                    diagnostics.Add(AssetValidation.Error(
                        "spriteforge.frame_out_of_bounds",
                        $"Frame '{frameId}' exceeds atlas bounds.",
                        sourcePath,
                        keyPath: keyPath,
                        span: context.GetSpan(keyPath)));
                }

                if (!string.IsNullOrWhiteSpace(frame.Pivot) && !SpriteForgePivots.IsSupported(frame.Pivot))
                {
                    diagnostics.Add(AssetValidation.Error(
                        "spriteforge.invalid_pivot",
                        $"Unsupported pivot '{frame.Pivot}'. Expected {string.Join(", ", SpriteForgePivots.Supported)}.",
                        sourcePath,
                        keyPath: $"{keyPath}.pivot",
                        span: context.GetSpan($"{keyPath}.pivot")));
                }
            }
        }

        private static void ValidateSprites(
            SpriteForgeAtlasTomlDocument asset,
            string sourcePath,
            List<AssetDiagnostic> diagnostics,
            AssetValidationContext context)
        {
            foreach (var (spriteId, sprite) in asset.Sprites)
            {
                var keyPath = $"sprites.{QuoteKey(spriteId)}";
                if (!string.IsNullOrWhiteSpace(sprite.Grid) && !asset.Grids.ContainsKey(sprite.Grid))
                {
                    diagnostics.Add(AssetValidation.Error(
                        "spriteforge.unknown_grid",
                        $"Sprite '{spriteId}' references unknown grid '{sprite.Grid}'.",
                        sourcePath,
                        keyPath: $"{keyPath}.grid",
                        span: context.GetSpan($"{keyPath}.grid")));
                }

                if (!string.IsNullOrWhiteSpace(sprite.Frame) && !asset.Frames.ContainsKey(sprite.Frame))
                {
                    diagnostics.Add(AssetValidation.Error(
                        "spriteforge.unknown_frame",
                        $"Sprite '{spriteId}' references unknown frame '{sprite.Frame}'.",
                        sourcePath,
                        keyPath: $"{keyPath}.frame",
                        span: context.GetSpan($"{keyPath}.frame")));
                }

                if (!string.IsNullOrWhiteSpace(sprite.Pivot) && !SpriteForgePivots.IsSupported(sprite.Pivot))
                {
                    diagnostics.Add(AssetValidation.Error(
                        "spriteforge.invalid_pivot",
                        $"Unsupported pivot '{sprite.Pivot}'. Expected {string.Join(", ", SpriteForgePivots.Supported)}.",
                        sourcePath,
                        keyPath: $"{keyPath}.pivot",
                        span: context.GetSpan($"{keyPath}.pivot")));
                }

                foreach (var (animationId, animation) in sprite.Animations)
                {
                    ValidateAnimation(asset, spriteId, animationId, animation, sourcePath, diagnostics, context);
                }
            }
        }

        private static void ValidateAnimation(
            SpriteForgeAtlasTomlDocument asset,
            string spriteId,
            string animationId,
            SpriteForgeAnimationTomlDocument animation,
            string sourcePath,
            List<AssetDiagnostic> diagnostics,
            AssetValidationContext context)
        {
            var keyPath = $"sprites.{QuoteKey(spriteId)}.animations.{QuoteKey(animationId)}";
            var resolvedGridId = NullIfWhiteSpace(animation.Grid) ?? NullIfWhiteSpace(asset.Sprites[spriteId].Grid);
            if (!string.IsNullOrWhiteSpace(resolvedGridId) && !asset.Grids.ContainsKey(resolvedGridId))
            {
                diagnostics.Add(AssetValidation.Error(
                    "spriteforge.unknown_animation_grid",
                    $"Sprite '{spriteId}' animation '{animationId}' references unknown grid '{resolvedGridId}'.",
                    sourcePath,
                    keyPath: $"{keyPath}.grid",
                    span: context.GetSpan($"{keyPath}.grid")));
            }

            if (!string.IsNullOrWhiteSpace(resolvedGridId) && asset.Grids.TryGetValue(resolvedGridId, out var grid))
            {
                var resolvedRow = animation.Row ?? asset.Sprites[spriteId].Row;
                if (resolvedRow.HasValue && (resolvedRow.Value < 0 || resolvedRow.Value >= grid.Rows))
                {
                    diagnostics.Add(AssetValidation.Error(
                        "spriteforge.animation_row_out_of_bounds",
                        $"Sprite '{spriteId}' animation '{animationId}' row {resolvedRow.Value} is outside grid '{resolvedGridId}' rows 0..{grid.Rows - 1}.",
                        sourcePath,
                        keyPath: $"{keyPath}.row",
                        span: context.GetSpan($"{keyPath}.row")));
                }

                for (var i = 0; i < animation.Frames.Count; i++)
                {
                    var frameKeyPath = $"{keyPath}.frames[{i}]";
                    if (!TryConvertFrameRef(animation.Frames[i], frameKeyPath, sourcePath, diagnostics, context.SourceMap, out var frameRef) || frameRef is null)
                        continue;

                    if (!string.IsNullOrWhiteSpace(frameRef.Frame))
                    {
                        if (!asset.Frames.ContainsKey(frameRef.Frame))
                        {
                            diagnostics.Add(AssetValidation.Error(
                                "spriteforge.unknown_frame",
                                $"Sprite '{spriteId}' animation '{animationId}' references unknown frame '{frameRef.Frame}'.",
                                sourcePath,
                                keyPath: frameKeyPath,
                                span: context.GetSpan(frameKeyPath)));
                        }

                        continue;
                    }

                    var frameGridId = frameRef.Grid ?? resolvedGridId;
                    if (string.IsNullOrWhiteSpace(frameGridId) || !asset.Grids.TryGetValue(frameGridId, out var frameGrid))
                        continue;

                    var frameRow = frameRef.Row ?? resolvedRow;
                    if (!frameRow.HasValue)
                    {
                        diagnostics.Add(AssetValidation.Error(
                            "spriteforge.animation_row_missing",
                            $"Sprite '{spriteId}' animation '{animationId}' frame {i} requires a row.",
                            sourcePath,
                            keyPath: frameKeyPath,
                            span: context.GetSpan(frameKeyPath)));
                    }
                    else if (frameRow.Value < 0 || frameRow.Value >= frameGrid.Rows)
                    {
                        diagnostics.Add(AssetValidation.Error(
                            "spriteforge.animation_row_out_of_bounds",
                            $"Sprite '{spriteId}' animation '{animationId}' row {frameRow.Value} is outside grid '{frameGridId}' rows 0..{frameGrid.Rows - 1}.",
                            sourcePath,
                            keyPath: frameKeyPath,
                            span: context.GetSpan(frameKeyPath)));
                    }

                    if (frameRef.Col.HasValue && (frameRef.Col.Value < 0 || frameRef.Col.Value >= frameGrid.Columns))
                    {
                        diagnostics.Add(AssetValidation.Error(
                            "spriteforge.frame_index_out_of_bounds",
                            $"Sprite '{spriteId}' animation '{animationId}' frame column {frameRef.Col.Value} is outside grid '{frameGridId}' columns 0..{frameGrid.Columns - 1}.",
                            sourcePath,
                            keyPath: frameKeyPath,
                            span: context.GetSpan(frameKeyPath)));
                    }
                }
            }
        }

        private static void RequirePositive(
            int value,
            string keyPath,
            string code,
            string message,
            string sourcePath,
            List<AssetDiagnostic> diagnostics,
            AssetValidationContext context)
        {
            if (value > 0)
                return;

            diagnostics.Add(AssetValidation.Error(
                code,
                message,
                sourcePath,
                keyPath: keyPath,
                span: context.GetSpan(keyPath)));
        }

        private static int ComputeGridPixelWidth(SpriteForgeGridTomlDocument grid) =>
            (grid.Columns * grid.CellWidth) + (Math.Max(grid.Columns - 1, 0) * (grid.GapX ?? 0));

        private static int ComputeGridPixelHeight(SpriteForgeGridTomlDocument grid) =>
            (grid.Rows * grid.CellHeight) + (Math.Max(grid.Rows - 1, 0) * (grid.GapY ?? 0));
    }

    private sealed record SpriteForgeAtlasTomlDocument
    {
        public SpriteForgeAtlasSection? Atlas { get; init; }

        public Dictionary<string, SpriteForgeGridTomlDocument> Grids { get; init; } = new(StringComparer.Ordinal);

        public Dictionary<string, SpriteForgeSpriteTomlDocument> Sprites { get; init; } = new(StringComparer.Ordinal);

        public Dictionary<string, SpriteForgeFrameTomlDocument> Frames { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed record SpriteForgeAtlasSection
    {
        public string Image { get; init; } = string.Empty;

        public int Width { get; init; }

        public int Height { get; init; }
    }

    private sealed record SpriteForgeGridTomlDocument
    {
        public int OriginX { get; init; }

        public int OriginY { get; init; }

        public int Columns { get; init; }

        public int Rows { get; init; }

        public int CellWidth { get; init; }

        public int CellHeight { get; init; }

        public string? DefaultPivot { get; init; }

        public int? GapX { get; init; }

        public int? GapY { get; init; }
    }

    private sealed record SpriteForgeSpriteTomlDocument
    {
        public string? Kind { get; init; }

        public string? DisplayName { get; init; }

        public string? Grid { get; init; }

        public int? Row { get; init; }

        public int? Col { get; init; }

        public string? Frame { get; init; }

        public float? Scale { get; init; }

        public int? OffsetX { get; init; }

        public int? OffsetY { get; init; }

        public string? Pivot { get; init; }

        public Dictionary<string, SpriteForgeAnimationTomlDocument> Animations { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed record SpriteForgeAnimationTomlDocument
    {
        public string? Grid { get; init; }

        public int? Row { get; init; }

        public List<object> Frames { get; init; } = [];

        public float? Fps { get; init; }

        public bool? Loop { get; init; }
    }

    private sealed record SpriteForgeFrameTomlDocument
    {
        public int X { get; init; }

        public int Y { get; init; }

        public int Width { get; init; }

        public int Height { get; init; }

        public string? Pivot { get; init; }

        public int? OffsetX { get; init; }

        public int? OffsetY { get; init; }

        public float? Scale { get; init; }
    }
}
