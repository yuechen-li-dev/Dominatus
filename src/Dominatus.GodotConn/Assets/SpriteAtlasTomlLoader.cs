using Dominatus.Assets.Toml;
using Godot;
using System.Buffers.Binary;

namespace Dominatus.GodotConn.Assets;

public static class SpriteAtlasTomlLoader
{
    public static SpriteAtlasLoadResult LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var rawToml = File.ReadAllText(path);
        if (LooksLikeCompiledRuntimeToml(path, rawToml))
            return LoadCompiledFile(path);

        var loadResult = TomlAssetLoader.LoadFile<SpriteAtlasTomlDocument>(
            path,
            new SpriteAtlasTomlValidator());
        if (loadResult.Value is null)
        {
            return new SpriteAtlasLoadResult
            {
                Asset = null,
                Diagnostics = loadResult.Diagnostics
            };
        }

        var diagnostics = new List<AssetDiagnostic>(loadResult.Diagnostics);
        try
        {
            var asset = BuildAsset(path, loadResult.Value, diagnostics, loadResult.SourceMap);
            return new SpriteAtlasLoadResult
            {
                Asset = diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error) ? null : asset,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            diagnostics.Add(AssetValidation.Error("sprite.transform", ex.Message, path));
            return new SpriteAtlasLoadResult
            {
                Asset = null,
                Diagnostics = diagnostics
            };
        }
    }

    private static SpriteAtlasLoadResult LoadCompiledFile(string path)
    {
        var loadResult = TomlAssetLoader.LoadFile<CompiledSpriteAtlasTomlDocument>(
            path,
            new CompiledSpriteAtlasTomlValidator());
        if (loadResult.Value is null)
        {
            return new SpriteAtlasLoadResult
            {
                Asset = null,
                Diagnostics = loadResult.Diagnostics
            };
        }

        var diagnostics = new List<AssetDiagnostic>(loadResult.Diagnostics);
        try
        {
            var asset = BuildCompiledAsset(path, loadResult.Value, diagnostics, loadResult.SourceMap);
            return new SpriteAtlasLoadResult
            {
                Asset = diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error) ? null : asset,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            diagnostics.Add(AssetValidation.Error("sprite.transform", ex.Message, path));
            return new SpriteAtlasLoadResult
            {
                Asset = null,
                Diagnostics = diagnostics
            };
        }
    }

    public static string GetMetadataPathForImage(string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        var directory = Path.GetDirectoryName(imagePath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(imagePath);
        var compiledPath = Path.Combine(directory, $"{fileName}.compiled.sprite.toml");
        if (File.Exists(compiledPath))
            return compiledPath;

        return Path.Combine(directory, $"{fileName}.sprite.toml");
    }

    private static SpriteAtlasAsset BuildAsset(
        string sourcePath,
        SpriteAtlasTomlDocument document,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap)
    {
        var atlas = document.Atlas ?? throw new InvalidOperationException("Sprite atlas TOML is missing the [atlas] table.");
        var resolvedImagePath = ResolveImagePath(sourcePath, atlas.Image);
        var defaultPivot = ParsePivot(atlas.DefaultPivot, "atlas.default_pivot", sourcePath, diagnostics, sourceMap);

        var frames = BuildFrames(document, sourcePath, diagnostics, sourceMap);
        var entities = BuildEntities(document, atlas, frames, sourcePath, diagnostics, sourceMap, defaultPivot);

        if (!TryReadImageDimensions(resolvedImagePath, out var imageWidth, out var imageHeight, out var imageError))
            throw new InvalidOperationException($"Sprite atlas image '{resolvedImagePath}' could not be inspected: {imageError}");

        return new SpriteAtlasAsset
        {
            SourcePath = sourcePath,
            ImagePath = atlas.Image.Trim(),
            ResolvedImagePath = resolvedImagePath,
            Width = imageWidth,
            Height = imageHeight,
            Grid = new SpriteAtlasGrid(atlas.Columns, atlas.Rows, atlas.CellWidth, atlas.CellHeight),
            DefaultPivot = defaultPivot,
            Entities = entities,
            Frames = frames
        };
    }

    private static SpriteAtlasAsset BuildCompiledAsset(
        string sourcePath,
        CompiledSpriteAtlasTomlDocument document,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap)
    {
        var atlas = document.Atlas ?? throw new InvalidOperationException("Compiled sprite atlas TOML is missing the [atlas] table.");
        var resolvedImagePath = ResolveImagePath(sourcePath, atlas.Image);
        var defaultPivot = ParsePivot(atlas.DefaultPivot, "atlas.default_pivot", sourcePath, diagnostics, sourceMap) ?? SpritePivot.BottomCenter;

        if (!TryReadImageDimensions(resolvedImagePath, out var imageWidth, out var imageHeight, out var imageError))
            throw new InvalidOperationException($"Sprite atlas image '{resolvedImagePath}' could not be inspected: {imageError}");

        var inferredGrid = InferCompiledGrid(document, atlas);
        var frames = BuildCompiledFrames(document, sourcePath, diagnostics, sourceMap, defaultPivot);
        var entities = BuildCompiledSprites(document, frames, sourcePath, diagnostics, sourceMap, defaultPivot);

        return new SpriteAtlasAsset
        {
            SourcePath = sourcePath,
            ImagePath = atlas.Image.Trim(),
            ResolvedImagePath = resolvedImagePath,
            Width = imageWidth,
            Height = imageHeight,
            Grid = inferredGrid,
            DefaultPivot = defaultPivot,
            Entities = entities,
            Frames = frames
        };
    }

    private static IReadOnlyDictionary<string, SpriteFrameAsset> BuildFrames(
        SpriteAtlasTomlDocument document,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap)
    {
        if (document.Frames is null || document.Frames.Count == 0)
            return new Dictionary<string, SpriteFrameAsset>(StringComparer.Ordinal);

        var result = new Dictionary<string, SpriteFrameAsset>(StringComparer.Ordinal);
        foreach (var (frameId, frameDoc) in document.Frames)
        {
            var correction = BuildCorrection(frameDoc, $"frames.{QuoteKey(frameId)}", sourcePath, diagnostics, sourceMap, null);
            result[frameId] = new SpriteFrameAsset
            {
                Id = frameId,
                Row = frameDoc.Row,
                Col = frameDoc.Col,
                Correction = correction
            };
        }

        return result;
    }

    private static IReadOnlyDictionary<string, SpriteEntityAsset> BuildEntities(
        SpriteAtlasTomlDocument document,
        SpriteAtlasSection atlas,
        IReadOnlyDictionary<string, SpriteFrameAsset> frames,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap,
        SpritePivot? defaultPivot)
    {
        var result = new Dictionary<string, SpriteEntityAsset>(StringComparer.Ordinal);
        foreach (var (entityId, entityDoc) in document.Entities)
        {
            var entityKey = $"entities.{entityId}";
            var entityPivot = ParsePivot(entityDoc.Pivot, $"{entityKey}.pivot", sourcePath, diagnostics, sourceMap) ?? defaultPivot;
            var entityCorrection = BuildCorrection(entityDoc.Correction, $"{entityKey}.correction", sourcePath, diagnostics, sourceMap, entityPivot);
            var animations = BuildAnimations(entityId, entityDoc, atlas, frames, sourcePath, diagnostics, sourceMap, entityPivot, entityCorrection);

            SpriteFrameRef? staticFrame = null;
            if (entityDoc.Row.HasValue && entityDoc.Col.HasValue)
            {
                staticFrame = CreateFrameRef(
                    entityId,
                    animationName: null,
                    frameIndex: 0,
                    row: entityDoc.Row.Value,
                    col: entityDoc.Col.Value,
                    frameId: null,
                    baseCorrection: entityCorrection,
                    declaredPivot: entityPivot,
                    sharedFrames: frames,
                    sourcePath,
                    diagnostics,
                    sourceMap,
                    keyPath: $"{entityKey}.col");
            }

            result[entityId] = new SpriteEntityAsset
            {
                Id = entityId,
                Kind = entityDoc.Kind?.Trim() ?? string.Empty,
                DisplayName = string.IsNullOrWhiteSpace(entityDoc.DisplayName) ? null : entityDoc.DisplayName.Trim(),
                Animations = animations,
                StaticFrame = staticFrame,
                Scale = entityDoc.Scale ?? 1f,
                Offset = new Vector2(entityDoc.OffsetX ?? 0f, entityDoc.OffsetY ?? 0f),
                Pivot = entityPivot,
                Correction = entityCorrection
            };
        }

        return result;
    }

    private static IReadOnlyDictionary<string, SpriteFrameAsset> BuildCompiledFrames(
        CompiledSpriteAtlasTomlDocument document,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap,
        SpritePivot defaultPivot)
    {
        var result = new Dictionary<string, SpriteFrameAsset>(StringComparer.Ordinal);

        foreach (var (stackframeId, stackframe) in document.Stackframes)
        {
            foreach (var frame in ExpandStackframe(stackframeId, stackframe, sourcePath, diagnostics, sourceMap, defaultPivot))
                result[frame.Id] = frame;
        }

        foreach (var (frameId, frame) in document.Frames)
            result[frameId] = BuildCompiledFrameAsset(frameId, frame, sourcePath, diagnostics, sourceMap, defaultPivot, $"frames.{QuoteKey(frameId)}");

        return result;
    }

    private static IReadOnlyDictionary<string, SpriteEntityAsset> BuildCompiledSprites(
        CompiledSpriteAtlasTomlDocument document,
        IReadOnlyDictionary<string, SpriteFrameAsset> frames,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap,
        SpritePivot defaultPivot)
    {
        var result = new Dictionary<string, SpriteEntityAsset>(StringComparer.Ordinal);
        foreach (var (spriteId, sprite) in document.Sprites)
        {
            var keyPath = $"sprites.{QuoteKey(spriteId)}";
            var animations = new Dictionary<string, SpriteAnimationAsset>(StringComparer.Ordinal);
            if (sprite.Animations is not null)
            {
                foreach (var (animationId, animation) in sprite.Animations)
                {
                    var frameRefs = new List<SpriteFrameRef>(animation.Frames.Count);
                    for (var i = 0; i < animation.Frames.Count; i++)
                    {
                        var frameId = animation.Frames[i]?.Trim() ?? string.Empty;
                        if (TryResolveCompiledFrameRef(frameId, frames, sourcePath, diagnostics, sourceMap, $"{keyPath}.animations.{QuoteKey(animationId)}.frames[{i}]", defaultPivot, out var frameRef))
                            frameRefs.Add(frameRef);
                    }

                    animations[animationId] = new SpriteAnimationAsset
                    {
                        Name = animationId,
                        Frames = frameRefs,
                        Fps = animation.Fps ?? 0f,
                        Loop = animation.Loop ?? true
                    };
                }
            }

            SpriteFrameRef? staticFrame = null;
            if (!string.IsNullOrWhiteSpace(sprite.Frame)
                && TryResolveCompiledFrameRef(sprite.Frame, frames, sourcePath, diagnostics, sourceMap, $"{keyPath}.frame", defaultPivot, out var resolvedStaticFrame))
            {
                staticFrame = resolvedStaticFrame;
            }

            var staticFrameAsset = !string.IsNullOrWhiteSpace(sprite.Frame) && frames.TryGetValue(sprite.Frame, out var sharedFrame)
                ? sharedFrame
                : null;

            result[spriteId] = new SpriteEntityAsset
            {
                Id = spriteId,
                Kind = FirstNonEmpty(sprite.Kind, staticFrameAsset?.Kind),
                DisplayName = FirstNonEmpty(sprite.DisplayName, staticFrameAsset?.DisplayName),
                Animations = animations,
                StaticFrame = staticFrame,
                Scale = 1f,
                Offset = Vector2.Zero,
                Pivot = defaultPivot,
                Correction = null
            };
        }

        return result;
    }

    private static IReadOnlyDictionary<string, SpriteAnimationAsset> BuildAnimations(
        string entityId,
        SpriteEntityTomlDocument entityDoc,
        SpriteAtlasSection atlas,
        IReadOnlyDictionary<string, SpriteFrameAsset> frames,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap,
        SpritePivot? entityPivot,
        SpriteFrameCorrection? entityCorrection)
    {
        var result = new Dictionary<string, SpriteAnimationAsset>(StringComparer.Ordinal);
        if (entityDoc.Animations is null)
            return result;

        foreach (var (animationName, animationDoc) in entityDoc.Animations)
        {
            var animationKey = $"entities.{entityId}.animations.{animationName}";
            var frameRefs = new List<SpriteFrameRef>(animationDoc.Frames.Count);
            for (var i = 0; i < animationDoc.Frames.Count; i++)
            {
                var column = animationDoc.Frames[i];
                var frameKey = $"{animationKey}.frames[{i}]";
                frameRefs.Add(CreateFrameRef(
                    entityId,
                    animationName,
                    i,
                    entityDoc.Row ?? throw new InvalidOperationException($"Entity '{entityId}' animation '{animationName}' requires an entity row."),
                    column,
                    frameId: $"{entityId}.{animationName}.{i}",
                    baseCorrection: entityCorrection,
                    declaredPivot: entityPivot,
                    sharedFrames: frames,
                    sourcePath,
                    diagnostics,
                    sourceMap,
                    keyPath: frameKey));
            }

            result[animationName] = new SpriteAnimationAsset
            {
                Name = animationName,
                Frames = frameRefs,
                Fps = animationDoc.Fps ?? 0f,
                Loop = animationDoc.Loop ?? true
            };
        }

        return result;
    }

    private static SpriteFrameRef CreateFrameRef(
        string entityId,
        string? animationName,
        int frameIndex,
        int row,
        int col,
        string? frameId,
        SpriteFrameCorrection? baseCorrection,
        SpritePivot? declaredPivot,
        IReadOnlyDictionary<string, SpriteFrameAsset> sharedFrames,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap,
        string keyPath)
    {
        if (!string.IsNullOrWhiteSpace(frameId) && sharedFrames.TryGetValue(frameId, out var sharedFrame))
        {
            return new SpriteFrameRef
            {
                Row = sharedFrame.Row,
                Col = sharedFrame.Col,
                FrameId = frameId,
                Correction = MergeCorrection(baseCorrection, sharedFrame.Correction, declaredPivot)
            };
        }

        if (!string.IsNullOrWhiteSpace(frameId) && animationName is not null && !sharedFrames.ContainsKey(frameId))
        {
            var fallbackFrameId = $"{entityId}.{animationName}.{col}";
            if (sharedFrames.TryGetValue(fallbackFrameId, out var numberedFrame))
            {
                return new SpriteFrameRef
                {
                    Row = numberedFrame.Row,
                    Col = numberedFrame.Col,
                    FrameId = fallbackFrameId,
                    Correction = MergeCorrection(baseCorrection, numberedFrame.Correction, declaredPivot)
                };
            }
        }

        return new SpriteFrameRef
        {
            Row = row,
            Col = col,
            FrameId = frameId,
            Correction = baseCorrection is null ? null : MergeCorrection(null, baseCorrection, declaredPivot)
        };
    }

    private static IEnumerable<SpriteFrameAsset> ExpandStackframe(
        string stackframeId,
        CompiledStackframeTomlDocument stackframe,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap,
        SpritePivot defaultPivot)
    {
        var labels = stackframe.Labels ?? [];
        for (var i = 0; i < stackframe.Count; i++)
        {
            var label = i < labels.Count && !string.IsNullOrWhiteSpace(labels[i])
                ? labels[i].Trim()
                : $"{stackframeId}.{i}";
            var rect = BuildStackframeRect(stackframe, i);
            yield return new SpriteFrameAsset
            {
                Id = label,
                Row = 0,
                Col = 0,
                Kind = string.Empty,
                DisplayName = null,
                Correction = new SpriteFrameCorrection
                {
                    SourceRectOverride = rect,
                    Offset = Vector2.Zero,
                    Scale = 1f,
                    Pivot = defaultPivot
                }
            };
        }
    }

    private static SpriteFrameAsset BuildCompiledFrameAsset(
        string frameId,
        CompiledFrameTomlDocument frame,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap,
        SpritePivot defaultPivot,
        string keyPath)
    {
        var pivot = ParsePivot(frame.Pivot, $"{keyPath}.pivot", sourcePath, diagnostics, sourceMap) ?? defaultPivot;
        return new SpriteFrameAsset
        {
            Id = frameId,
            Row = 0,
            Col = 0,
            Kind = frame.Kind?.Trim() ?? string.Empty,
            DisplayName = string.IsNullOrWhiteSpace(frame.DisplayName) ? null : frame.DisplayName.Trim(),
            Correction = new SpriteFrameCorrection
            {
                SourceRectOverride = new Rect2(frame.X, frame.Y, frame.Width, frame.Height),
                Offset = Vector2.Zero,
                Scale = 1f,
                Pivot = pivot
            }
        };
    }

    private static bool TryResolveCompiledFrameRef(
        string frameId,
        IReadOnlyDictionary<string, SpriteFrameAsset> frames,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap,
        string keyPath,
        SpritePivot defaultPivot,
        out SpriteFrameRef frameRef)
    {
        if (frames.TryGetValue(frameId, out var frame))
        {
            frameRef = new SpriteFrameRef
            {
                Row = frame.Row,
                Col = frame.Col,
                FrameId = frame.Id,
                Correction = frame.Correction ?? new SpriteFrameCorrection { Pivot = defaultPivot }
            };
            return true;
        }

        diagnostics.Add(AssetValidation.Error(
            "sprite.unknown_frame",
            $"Sprite metadata references unknown frame '{frameId}'.",
            sourcePath,
            keyPath: keyPath,
            span: sourceMap?.TryGetSpan(keyPath, out var span) == true ? span : null));
        frameRef = null!;
        return false;
    }

    private static Rect2 BuildStackframeRect(CompiledStackframeTomlDocument stackframe, int index)
    {
        var offset = stackframe.Step * index;
        return stackframe.Direction.Trim().Equals("horizontal", StringComparison.OrdinalIgnoreCase)
            ? new Rect2(stackframe.X + offset, stackframe.Y, stackframe.Width, stackframe.Height)
            : new Rect2(stackframe.X, stackframe.Y + offset, stackframe.Width, stackframe.Height);
    }

    private static SpriteAtlasGrid InferCompiledGrid(CompiledSpriteAtlasTomlDocument document, CompiledSpriteAtlasSection atlas)
    {
        var verticalStackframes = document.Stackframes.Values
            .Where(x => !x.Direction.Trim().Equals("horizontal", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var cellHeight = verticalStackframes
            .Select(x => x.Step > 0 ? x.Step : x.Height)
            .FirstOrDefault(x => x > 0);
        if (cellHeight <= 0)
            cellHeight = atlas.Height;

        var rows = atlas.Height > 0 && atlas.Height % cellHeight == 0
            ? atlas.Height / cellHeight
            : 1;

        var columns = verticalStackframes.Length;
        if (columns <= 0)
            columns = 1;

        var cellWidth = atlas.Width > 0 && atlas.Width % columns == 0
            ? atlas.Width / columns
            : atlas.Width;

        return new SpriteAtlasGrid(columns, rows, cellWidth, cellHeight);
    }

    private static SpriteFrameCorrection? BuildCorrection(
        ICorrectionTomlDocument? correctionDoc,
        string keyPath,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap,
        SpritePivot? inheritedPivot)
    {
        if (correctionDoc is null)
            return inheritedPivot is null ? null : new SpriteFrameCorrection { Pivot = inheritedPivot };

        var pivot = ParsePivot(correctionDoc.Pivot, $"{keyPath}.pivot", sourcePath, diagnostics, sourceMap) ?? inheritedPivot;
        Rect2? sourceRect = null;
        if (correctionDoc.SourceRectX.HasValue
            || correctionDoc.SourceRectY.HasValue
            || correctionDoc.SourceRectWidth.HasValue
            || correctionDoc.SourceRectHeight.HasValue)
        {
            if (!correctionDoc.SourceRectX.HasValue
                || !correctionDoc.SourceRectY.HasValue
                || !correctionDoc.SourceRectWidth.HasValue
                || !correctionDoc.SourceRectHeight.HasValue)
            {
                diagnostics.Add(AssetValidation.Error(
                    "sprite.correction_source_rect_incomplete",
                    "source_rect_override requires x, y, width, and height values.",
                    sourcePath,
                    keyPath: keyPath,
                    span: sourceMap?.TryGetSpan(keyPath, out var span) == true ? span : null));
            }
            else
            {
                sourceRect = new Rect2(
                    correctionDoc.SourceRectX.Value,
                    correctionDoc.SourceRectY.Value,
                    correctionDoc.SourceRectWidth.Value,
                    correctionDoc.SourceRectHeight.Value);
            }
        }

        Rect2? trim = null;
        if (correctionDoc.TrimX.HasValue
            || correctionDoc.TrimY.HasValue
            || correctionDoc.TrimWidth.HasValue
            || correctionDoc.TrimHeight.HasValue)
        {
            if (!correctionDoc.TrimX.HasValue
                || !correctionDoc.TrimY.HasValue
                || !correctionDoc.TrimWidth.HasValue
                || !correctionDoc.TrimHeight.HasValue)
            {
                diagnostics.Add(AssetValidation.Error(
                    "sprite.correction_trim_incomplete",
                    "trim requires x, y, width, and height values.",
                    sourcePath,
                    keyPath: keyPath,
                    span: sourceMap?.TryGetSpan(keyPath, out var span) == true ? span : null));
            }
            else
            {
                trim = new Rect2(
                    correctionDoc.TrimX.Value,
                    correctionDoc.TrimY.Value,
                    correctionDoc.TrimWidth.Value,
                    correctionDoc.TrimHeight.Value);
            }
        }

        return new SpriteFrameCorrection
        {
            SourceRectOverride = sourceRect,
            Offset = new Vector2(correctionDoc.OffsetX ?? 0f, correctionDoc.OffsetY ?? 0f),
            Scale = correctionDoc.Scale ?? 1f,
            Pivot = pivot,
            Trim = trim
        };
    }

    private static SpriteFrameCorrection MergeCorrection(
        SpriteFrameCorrection? baseCorrection,
        SpriteFrameCorrection? overrideCorrection,
        SpritePivot? fallbackPivot)
    {
        if (baseCorrection is null && overrideCorrection is null && fallbackPivot is null)
            return null!;

        var baseValue = baseCorrection ?? new SpriteFrameCorrection { Pivot = fallbackPivot };
        if (overrideCorrection is null)
        {
            return baseCorrection is null && fallbackPivot is not null
                ? new SpriteFrameCorrection
                {
                    SourceRectOverride = baseValue.SourceRectOverride,
                    Offset = baseValue.Offset,
                    Scale = baseValue.Scale,
                    Pivot = baseValue.Pivot ?? fallbackPivot,
                    Trim = baseValue.Trim
                }
                : baseValue;
        }

        return new SpriteFrameCorrection
        {
            SourceRectOverride = overrideCorrection.SourceRectOverride ?? baseValue.SourceRectOverride,
            Offset = baseValue.Offset + overrideCorrection.Offset,
            Scale = baseValue.Scale * overrideCorrection.Scale,
            Pivot = overrideCorrection.Pivot ?? baseValue.Pivot ?? fallbackPivot,
            Trim = overrideCorrection.Trim ?? baseValue.Trim
        };
    }

    private static SpritePivot? ParsePivot(
        string? raw,
        string keyPath,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Trim().ToLowerInvariant() switch
        {
            "center" => SpritePivot.Center,
            "bottom_center" => SpritePivot.BottomCenter,
            "top_left" => SpritePivot.TopLeft,
            "top_center" => SpritePivot.TopCenter,
            _ => AddInvalidPivot(raw, keyPath, sourcePath, diagnostics, sourceMap)
        };
    }

    private static SpritePivot? AddInvalidPivot(
        string raw,
        string keyPath,
        string sourcePath,
        List<AssetDiagnostic> diagnostics,
        TomlAssetSourceMap? sourceMap)
    {
        diagnostics.Add(AssetValidation.Error(
            "sprite.invalid_pivot",
            $"Unsupported pivot '{raw}'. Expected center, bottom_center, top_left, or top_center.",
            sourcePath,
            keyPath: keyPath,
            span: sourceMap?.TryGetSpan(keyPath, out var span) == true ? span : null));
        return null;
    }

    private static string ResolveImagePath(string sourcePath, string imagePath)
    {
        var trimmed = imagePath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("Sprite atlas image path is required.");

        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed);

        var directory = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(directory, trimmed));
    }

    private static string QuoteKey(string value) => $"\"{value}\"";

    private static bool LooksLikeCompiledRuntimeToml(string path, string rawToml)
    {
        return path.Contains(".compiled.sprite.toml", StringComparison.OrdinalIgnoreCase)
            || rawToml.Contains("[sprites.", StringComparison.Ordinal)
            || rawToml.Contains("[stackframes.", StringComparison.Ordinal);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
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

    private sealed class SpriteAtlasTomlValidator : IAssetValidator<SpriteAtlasTomlDocument>
    {
        public IReadOnlyList<AssetDiagnostic> Validate(SpriteAtlasTomlDocument asset, AssetValidationContext context)
        {
            var diagnostics = new List<AssetDiagnostic>();
            var sourcePath = context.SourcePath ?? string.Empty;
            if (asset.Atlas is null)
            {
                diagnostics.Add(AssetValidation.Required("atlas", sourcePath, "atlas"));
                return diagnostics;
            }

            ValidateAtlas(asset.Atlas, sourcePath, diagnostics, context);
            ValidateEntities(asset, sourcePath, diagnostics, context);
            ValidateFrames(asset, sourcePath, diagnostics, context);
            return diagnostics;
        }

        private static void ValidateAtlas(
            SpriteAtlasSection atlas,
            string sourcePath,
            List<AssetDiagnostic> diagnostics,
            AssetValidationContext context)
        {
            if (string.IsNullOrWhiteSpace(atlas.Image))
                diagnostics.Add(AssetValidation.Required("atlas.image", sourcePath, "atlas.image"));

            RequirePositive(atlas.Width, "atlas.width", "sprite.width_invalid", "Sprite atlas width must be greater than zero.", sourcePath, diagnostics, context);
            RequirePositive(atlas.Height, "atlas.height", "sprite.height_invalid", "Sprite atlas height must be greater than zero.", sourcePath, diagnostics, context);
            RequirePositive(atlas.Columns, "atlas.columns", "sprite.columns_invalid", "Sprite atlas columns must be greater than zero.", sourcePath, diagnostics, context);
            RequirePositive(atlas.Rows, "atlas.rows", "sprite.rows_invalid", "Sprite atlas rows must be greater than zero.", sourcePath, diagnostics, context);
            RequirePositive(atlas.CellWidth, "atlas.cell_width", "sprite.cell_width_invalid", "Sprite atlas cell_width must be greater than zero.", sourcePath, diagnostics, context);
            RequirePositive(atlas.CellHeight, "atlas.cell_height", "sprite.cell_height_invalid", "Sprite atlas cell_height must be greater than zero.", sourcePath, diagnostics, context);

            if (diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error))
                return;

            if (atlas.Columns * atlas.CellWidth != atlas.Width)
            {
                diagnostics.Add(AssetValidation.Error(
                    "sprite.grid_width_mismatch",
                    $"Atlas width {atlas.Width} does not match columns * cell_width ({atlas.Columns * atlas.CellWidth}).",
                    sourcePath,
                    keyPath: "atlas.columns",
                    span: context.GetSpan("atlas.columns")));
            }

            if (atlas.Rows * atlas.CellHeight != atlas.Height)
            {
                diagnostics.Add(AssetValidation.Error(
                    "sprite.grid_height_mismatch",
                    $"Atlas height {atlas.Height} does not match rows * cell_height ({atlas.Rows * atlas.CellHeight}).",
                    sourcePath,
                    keyPath: "atlas.rows",
                    span: context.GetSpan("atlas.rows")));
            }

            var imagePath = ResolveImagePath(sourcePath, atlas.Image);
            if (!File.Exists(imagePath))
            {
                diagnostics.Add(AssetValidation.Error(
                    "sprite.image_missing",
                    $"Sprite atlas image '{imagePath}' does not exist.",
                    sourcePath,
                    keyPath: "atlas.image",
                    span: context.GetSpan("atlas.image")));
                return;
            }

            if (!TryReadImageDimensions(imagePath, out var width, out var height, out var error))
            {
                diagnostics.Add(AssetValidation.Error(
                    "sprite.image_load_failed",
                    $"Sprite atlas image '{imagePath}' could not be inspected: {error}",
                    sourcePath,
                    keyPath: "atlas.image",
                    span: context.GetSpan("atlas.image")));
                return;
            }

            if (width != atlas.Width || height != atlas.Height)
            {
                diagnostics.Add(AssetValidation.Error(
                    "sprite.image_dimension_mismatch",
                    $"Sprite atlas image is {width}x{height}, but TOML declares {atlas.Width}x{atlas.Height}.",
                    sourcePath,
                    keyPath: "atlas.width",
                    span: context.GetSpan("atlas.width")));
            }
        }

        private static void ValidateEntities(
            SpriteAtlasTomlDocument asset,
            string sourcePath,
            List<AssetDiagnostic> diagnostics,
            AssetValidationContext context)
        {
            if (asset.Entities.Count == 0)
            {
                diagnostics.Add(AssetValidation.Warning(
                    "sprite.entities_empty",
                    "Sprite atlas TOML does not define any entities.",
                    sourcePath,
                    keyPath: "entities",
                    span: context.GetSpan("entities")));
            }

            var atlas = asset.Atlas!;
            foreach (var (entityId, entity) in asset.Entities)
            {
                var entityKey = $"entities.{entityId}";
                if (string.IsNullOrWhiteSpace(entity.Kind))
                {
                    diagnostics.Add(AssetValidation.Warning(
                        "sprite.entity_kind_missing",
                        $"Entity '{entityId}' does not declare a kind.",
                        sourcePath,
                        keyPath: $"{entityKey}.kind",
                        span: context.GetSpan($"{entityKey}.kind")));
                }

                if (entity.Row.HasValue && (entity.Row.Value < 0 || entity.Row.Value >= atlas.Rows))
                {
                    diagnostics.Add(AssetValidation.Error(
                        "sprite.entity_row_out_of_bounds",
                        $"Entity '{entityId}' row {entity.Row.Value} is outside atlas rows 0..{atlas.Rows - 1}.",
                        sourcePath,
                        keyPath: $"{entityKey}.row",
                        span: context.GetSpan($"{entityKey}.row")));
                }

                if (entity.Col.HasValue && (entity.Col.Value < 0 || entity.Col.Value >= atlas.Columns))
                {
                    diagnostics.Add(AssetValidation.Error(
                        "sprite.entity_col_out_of_bounds",
                        $"Entity '{entityId}' col {entity.Col.Value} is outside atlas columns 0..{atlas.Columns - 1}.",
                        sourcePath,
                        keyPath: $"{entityKey}.col",
                        span: context.GetSpan($"{entityKey}.col")));
                }

                if (entity.Animations is not null)
                {
                    foreach (var (animationName, animation) in entity.Animations)
                    {
                        var animationKey = $"{entityKey}.animations.{animationName}";
                        if (!entity.Row.HasValue)
                        {
                            diagnostics.Add(AssetValidation.Error(
                                "sprite.entity_animation_missing_row",
                                $"Entity '{entityId}' animation '{animationName}' requires entity.row.",
                                sourcePath,
                                keyPath: $"{entityKey}.row",
                                span: context.GetSpan($"{entityKey}.row")));
                            continue;
                        }

                        if (animation.Frames.Count == 0)
                        {
                            diagnostics.Add(AssetValidation.Warning(
                                "sprite.animation_frames_empty",
                                $"Entity '{entityId}' animation '{animationName}' has no frames.",
                                sourcePath,
                                keyPath: $"{animationKey}.frames",
                                span: context.GetSpan($"{animationKey}.frames")));
                        }

                        for (var i = 0; i < animation.Frames.Count; i++)
                        {
                            var col = animation.Frames[i];
                            if (col < 0 || col >= atlas.Columns)
                            {
                                diagnostics.Add(AssetValidation.Error(
                                    "sprite.animation_col_out_of_bounds",
                                    $"Entity '{entityId}' animation '{animationName}' frame column {col} is outside atlas columns 0..{atlas.Columns - 1}.",
                                    sourcePath,
                                    keyPath: $"{animationKey}.frames[{i}]",
                                    span: context.GetSpan($"{animationKey}.frames[{i}]")));
                            }
                        }
                    }
                }
            }
        }

        private static void ValidateFrames(
            SpriteAtlasTomlDocument asset,
            string sourcePath,
            List<AssetDiagnostic> diagnostics,
            AssetValidationContext context)
        {
            var atlas = asset.Atlas!;
            foreach (var (frameId, frame) in asset.Frames)
            {
                var key = $"frames.{QuoteKey(frameId)}";
                if (frame.Row < 0 || frame.Row >= atlas.Rows)
                {
                    diagnostics.Add(AssetValidation.Error(
                        "sprite.frame_row_out_of_bounds",
                        $"Frame '{frameId}' row {frame.Row} is outside atlas rows 0..{atlas.Rows - 1}.",
                        sourcePath,
                        keyPath: $"{key}.row",
                        span: context.GetSpan($"{key}.row")));
                }

                if (frame.Col < 0 || frame.Col >= atlas.Columns)
                {
                    diagnostics.Add(AssetValidation.Error(
                        "sprite.frame_col_out_of_bounds",
                        $"Frame '{frameId}' col {frame.Col} is outside atlas columns 0..{atlas.Columns - 1}.",
                        sourcePath,
                        keyPath: $"{key}.col",
                        span: context.GetSpan($"{key}.col")));
                }
            }
        }

    }

    private sealed class CompiledSpriteAtlasTomlValidator : IAssetValidator<CompiledSpriteAtlasTomlDocument>
    {
        public IReadOnlyList<AssetDiagnostic> Validate(CompiledSpriteAtlasTomlDocument asset, AssetValidationContext context)
        {
            var diagnostics = new List<AssetDiagnostic>();
            var sourcePath = context.SourcePath ?? string.Empty;
            if (asset.Atlas is null)
            {
                diagnostics.Add(AssetValidation.Required("atlas", sourcePath, "atlas"));
                return diagnostics;
            }

            if (string.IsNullOrWhiteSpace(asset.Atlas.Image))
                diagnostics.Add(AssetValidation.Required("atlas.image", sourcePath, "atlas.image"));

            RequirePositive(asset.Atlas.Width, "atlas.width", "sprite.width_invalid", "Compiled sprite atlas width must be greater than zero.", sourcePath, diagnostics, context);
            RequirePositive(asset.Atlas.Height, "atlas.height", "sprite.height_invalid", "Compiled sprite atlas height must be greater than zero.", sourcePath, diagnostics, context);

            foreach (var (frameId, frame) in asset.Frames)
            {
                var key = $"frames.{QuoteKey(frameId)}";
                ValidateCompiledRect(frame.X, frame.Y, frame.Width, frame.Height, asset.Atlas, key, sourcePath, diagnostics, context);
            }

            foreach (var (stackframeId, stackframe) in asset.Stackframes)
            {
                var key = $"stackframes.{QuoteKey(stackframeId)}";
                RequirePositive(stackframe.Count, $"{key}.count", "sprite.stackframe_count_invalid", $"Stackframe '{stackframeId}' count must be greater than zero.", sourcePath, diagnostics, context);
                RequirePositive(stackframe.Step, $"{key}.step", "sprite.stackframe_step_invalid", $"Stackframe '{stackframeId}' step must be greater than zero.", sourcePath, diagnostics, context);
                for (var i = 0; i < Math.Max(stackframe.Count, 0); i++)
                {
                    var rect = BuildStackframeRect(stackframe, i);
                    ValidateCompiledRect((int)rect.Position.X, (int)rect.Position.Y, (int)rect.Size.X, (int)rect.Size.Y, asset.Atlas, key, sourcePath, diagnostics, context);
                }
            }

            return diagnostics;
        }

        private static void ValidateCompiledRect(
            int x,
            int y,
            int width,
            int height,
            CompiledSpriteAtlasSection atlas,
            string keyPath,
            string sourcePath,
            List<AssetDiagnostic> diagnostics,
            AssetValidationContext context)
        {
            RequirePositive(width, $"{keyPath}.width", "sprite.frame_width_invalid", "Frame width must be greater than zero.", sourcePath, diagnostics, context);
            RequirePositive(height, $"{keyPath}.height", "sprite.frame_height_invalid", "Frame height must be greater than zero.", sourcePath, diagnostics, context);
            if (x < 0 || y < 0 || x + width > atlas.Width || y + height > atlas.Height)
            {
                diagnostics.Add(AssetValidation.Error(
                    "sprite.frame_out_of_bounds",
                    $"Frame rectangle ({x}, {y}, {width}, {height}) is outside atlas bounds {atlas.Width}x{atlas.Height}.",
                    sourcePath,
                    keyPath: keyPath,
                    span: context.GetSpan(keyPath)));
            }
        }
    }

    private sealed record SpriteAtlasTomlDocument
    {
        public SpriteAtlasSection? Atlas { get; init; }

        public Dictionary<string, SpriteEntityTomlDocument> Entities { get; init; } = new(StringComparer.Ordinal);

        public Dictionary<string, SpriteFrameTomlDocument> Frames { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed record CompiledSpriteAtlasTomlDocument
    {
        public CompiledSpriteAtlasSection? Atlas { get; init; }

        public Dictionary<string, CompiledFrameTomlDocument> Frames { get; init; } = new(StringComparer.Ordinal);

        public Dictionary<string, CompiledSpriteTomlDocument> Sprites { get; init; } = new(StringComparer.Ordinal);

        public Dictionary<string, CompiledStackframeTomlDocument> Stackframes { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed record SpriteAtlasSection
    {
        public string Image { get; init; } = string.Empty;

        public int Width { get; init; }

        public int Height { get; init; }

        public int Columns { get; init; }

        public int Rows { get; init; }

        public int CellWidth { get; init; }

        public int CellHeight { get; init; }

        public string? DefaultPivot { get; init; }
    }

    private sealed record CompiledSpriteAtlasSection
    {
        public string Image { get; init; } = string.Empty;

        public int Width { get; init; }

        public int Height { get; init; }

        public string? DefaultPivot { get; init; }
    }

    private sealed record SpriteEntityTomlDocument : ICorrectionTomlDocument
    {
        public string? Kind { get; init; }

        public string? DisplayName { get; init; }

        public int? Row { get; init; }

        public int? Col { get; init; }

        public float? Scale { get; init; }

        public float? OffsetX { get; init; }

        public float? OffsetY { get; init; }

        public string? Pivot { get; init; }

        public SpriteCorrectionTomlDocument? Correction { get; init; }

        public Dictionary<string, SpriteAnimationTomlDocument>? Animations { get; init; }

        float? ICorrectionTomlDocument.Scale => Correction?.Scale ?? Scale;
        float? ICorrectionTomlDocument.OffsetX => Correction?.OffsetX ?? OffsetX;
        float? ICorrectionTomlDocument.OffsetY => Correction?.OffsetY ?? OffsetY;
        string? ICorrectionTomlDocument.Pivot => Correction?.Pivot ?? Pivot;
        float? ICorrectionTomlDocument.SourceRectX => Correction?.SourceRectX;
        float? ICorrectionTomlDocument.SourceRectY => Correction?.SourceRectY;
        float? ICorrectionTomlDocument.SourceRectWidth => Correction?.SourceRectWidth;
        float? ICorrectionTomlDocument.SourceRectHeight => Correction?.SourceRectHeight;
        float? ICorrectionTomlDocument.TrimX => Correction?.TrimX;
        float? ICorrectionTomlDocument.TrimY => Correction?.TrimY;
        float? ICorrectionTomlDocument.TrimWidth => Correction?.TrimWidth;
        float? ICorrectionTomlDocument.TrimHeight => Correction?.TrimHeight;
    }

    private sealed record SpriteAnimationTomlDocument
    {
        public List<int> Frames { get; init; } = [];

        public float? Fps { get; init; }

        public bool? Loop { get; init; }
    }

    private sealed record CompiledSpriteTomlDocument
    {
        public string? DisplayName { get; init; }

        public string? Kind { get; init; }

        public string? Frame { get; init; }

        public Dictionary<string, CompiledSpriteAnimationTomlDocument>? Animations { get; init; }
    }

    private sealed record CompiledSpriteAnimationTomlDocument
    {
        public List<string> Frames { get; init; } = [];

        public float? Fps { get; init; }

        public bool? Loop { get; init; }
    }

    private sealed record CompiledFrameTomlDocument
    {
        public int X { get; init; }

        public int Y { get; init; }

        public int Width { get; init; }

        public int Height { get; init; }

        public string? DisplayName { get; init; }

        public string? SpriteId { get; init; }

        public string? AnimationId { get; init; }

        public string? SourceKind { get; init; }

        public string? Pivot { get; init; }

        public string? Kind { get; init; }

        public string? SourceStackframe { get; init; }

        public int? SourceStackIndex { get; init; }
    }

    private sealed record CompiledStackframeTomlDocument
    {
        public int X { get; init; }

        public int Y { get; init; }

        public int Width { get; init; }

        public int Height { get; init; }

        public int Count { get; init; }

        public string Direction { get; init; } = "vertical";

        public int Step { get; init; }

        public List<string>? Labels { get; init; }

        public string? Description { get; init; }
    }

    private sealed record SpriteFrameTomlDocument : ICorrectionTomlDocument
    {
        public int Row { get; init; }

        public int Col { get; init; }

        public float? OffsetX { get; init; }

        public float? OffsetY { get; init; }

        public float? Scale { get; init; }

        public string? Pivot { get; init; }

        public float? SourceRectX { get; init; }

        public float? SourceRectY { get; init; }

        public float? SourceRectWidth { get; init; }

        public float? SourceRectHeight { get; init; }

        public float? TrimX { get; init; }

        public float? TrimY { get; init; }

        public float? TrimWidth { get; init; }

        public float? TrimHeight { get; init; }
    }

    private sealed record SpriteCorrectionTomlDocument : ICorrectionTomlDocument
    {
        public float? OffsetX { get; init; }

        public float? OffsetY { get; init; }

        public float? Scale { get; init; }

        public string? Pivot { get; init; }

        public float? SourceRectX { get; init; }

        public float? SourceRectY { get; init; }

        public float? SourceRectWidth { get; init; }

        public float? SourceRectHeight { get; init; }

        public float? TrimX { get; init; }

        public float? TrimY { get; init; }

        public float? TrimWidth { get; init; }

        public float? TrimHeight { get; init; }
    }

    private interface ICorrectionTomlDocument
    {
        float? OffsetX { get; }

        float? OffsetY { get; }

        float? Scale { get; }

        string? Pivot { get; }

        float? SourceRectX { get; }

        float? SourceRectY { get; }

        float? SourceRectWidth { get; }

        float? SourceRectHeight { get; }

        float? TrimX { get; }

        float? TrimY { get; }

        float? TrimWidth { get; }

        float? TrimHeight { get; }
    }

    private static bool TryReadImageDimensions(string path, out int width, out int height, out string error)
    {
        width = 0;
        height = 0;
        error = string.Empty;

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension switch
            {
                ".png" => TryReadPngDimensions(reader, out width, out height, out error),
                _ => Fail($"Unsupported image extension '{extension}'. PNG atlas metadata validation is currently supported.", out width, out height, out error)
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryReadPngDimensions(BinaryReader reader, out int width, out int height, out string error)
    {
        width = 0;
        height = 0;
        error = string.Empty;

        Span<byte> signature = stackalloc byte[8];
        if (reader.Read(signature) != signature.Length
            || !signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            error = "File is not a valid PNG.";
            return false;
        }

        Span<byte> chunkLength = stackalloc byte[4];
        Span<byte> chunkType = stackalloc byte[4];
        if (reader.Read(chunkLength) != chunkLength.Length || reader.Read(chunkType) != chunkType.Length)
        {
            error = "PNG header is truncated.";
            return false;
        }

        if (!chunkType.SequenceEqual("IHDR"u8))
        {
            error = "PNG is missing the IHDR header chunk.";
            return false;
        }

        Span<byte> dimensions = stackalloc byte[8];
        if (reader.Read(dimensions) != dimensions.Length)
        {
            error = "PNG IHDR chunk is truncated.";
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(dimensions[..4]);
        height = BinaryPrimitives.ReadInt32BigEndian(dimensions[4..]);
        return width > 0 && height > 0
            ? true
            : Fail("PNG width and height must be greater than zero.", out width, out height, out error);
    }

    private static bool Fail(string message, out int width, out int height, out string error)
    {
        width = 0;
        height = 0;
        error = message;
        return false;
    }
}
