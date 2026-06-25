namespace Dominatus.SpriteForge;

public sealed class SpriteForgeResolver
{
    public IReadOnlyList<SpriteForgeResolvedFrame> ResolveAnimation(
        SpriteForgeAtlas atlas,
        string spriteId,
        string animationId)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentException.ThrowIfNullOrWhiteSpace(spriteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(animationId);

        if (!atlas.Sprites.TryGetValue(spriteId, out var sprite))
            throw new KeyNotFoundException($"Sprite '{spriteId}' was not found.");

        if (!sprite.Animations.TryGetValue(animationId, out var animation))
            throw new KeyNotFoundException($"Sprite '{spriteId}' does not contain animation '{animationId}'.");

        var result = new List<SpriteForgeResolvedFrame>(animation.Frames.Count);
        for (var i = 0; i < animation.Frames.Count; i++)
        {
            result.Add(ResolveFrame(atlas, sprite, animationId, animation, animation.Frames[i], i));
        }

        return result;
    }

    public SpriteForgeResolvedFrame ResolveStaticSprite(SpriteForgeAtlas atlas, string spriteId)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentException.ThrowIfNullOrWhiteSpace(spriteId);

        if (!atlas.Sprites.TryGetValue(spriteId, out var sprite))
            throw new KeyNotFoundException($"Sprite '{spriteId}' was not found.");

        if (!string.IsNullOrWhiteSpace(sprite.Frame))
        {
            return ResolveAbsoluteFrame(atlas, sprite, animationId: null, frameId: sprite.Frame!, frameIndex: 0);
        }

        if (string.IsNullOrWhiteSpace(sprite.Grid) || !sprite.Row.HasValue || !sprite.Col.HasValue)
            throw new InvalidOperationException($"Sprite '{spriteId}' does not define a static grid cell or absolute frame.");

        return ResolveGridFrame(atlas, sprite, animationId: null, sprite.Grid, sprite.Row.Value, sprite.Col.Value, 0);
    }

    private SpriteForgeResolvedFrame ResolveFrame(
        SpriteForgeAtlas atlas,
        SpriteForgeSprite sprite,
        string animationId,
        SpriteForgeAnimation animation,
        SpriteForgeFrameRef frameRef,
        int frameIndex)
    {
        if (!string.IsNullOrWhiteSpace(frameRef.Frame))
        {
            return ResolveAbsoluteFrame(atlas, sprite, animationId, frameRef.Frame!, frameIndex);
        }

        var gridId = frameRef.Grid ?? animation.Grid ?? sprite.Grid
            ?? throw new InvalidOperationException($"Sprite '{sprite.Id}' animation '{animationId}' frame {frameIndex} does not resolve to a grid.");
        var row = frameRef.Row ?? animation.Row ?? sprite.Row
            ?? throw new InvalidOperationException($"Sprite '{sprite.Id}' animation '{animationId}' frame {frameIndex} does not resolve to a row.");
        var col = frameRef.Col
            ?? throw new InvalidOperationException($"Sprite '{sprite.Id}' animation '{animationId}' frame {frameIndex} does not resolve to a column.");

        return ResolveGridFrame(atlas, sprite, animationId, gridId, row, col, frameIndex);
    }

    private SpriteForgeResolvedFrame ResolveAbsoluteFrame(
        SpriteForgeAtlas atlas,
        SpriteForgeSprite sprite,
        string? animationId,
        string frameId,
        int frameIndex)
    {
        if (!atlas.Frames.TryGetValue(frameId, out var frame))
            throw new KeyNotFoundException($"Frame '{frameId}' was not found.");

        var pivot = SpriteForgePivots.Normalize(frame.Pivot ?? sprite.Pivot);
        var (pivotX, pivotY) = SpriteForgePivots.Resolve(pivot, frame.X, frame.Y, frame.Width, frame.Height);
        return new SpriteForgeResolvedFrame
        {
            SpriteId = sprite.Id,
            AnimationId = animationId,
            FrameIndex = frameIndex,
            X = frame.X,
            Y = frame.Y,
            Width = frame.Width,
            Height = frame.Height,
            OffsetX = sprite.OffsetX + frame.OffsetX,
            OffsetY = sprite.OffsetY + frame.OffsetY,
            Scale = sprite.Scale * frame.Scale,
            Pivot = pivot,
            PivotX = pivotX,
            PivotY = pivotY,
            Source = SpriteForgeResolvedFrameSource.AbsoluteFrame,
            FrameId = frameId
        };
    }

    private SpriteForgeResolvedFrame ResolveGridFrame(
        SpriteForgeAtlas atlas,
        SpriteForgeSprite sprite,
        string? animationId,
        string gridId,
        int row,
        int col,
        int frameIndex)
    {
        if (!atlas.Grids.TryGetValue(gridId, out var grid))
            throw new KeyNotFoundException($"Grid '{gridId}' was not found.");

        var x = grid.OriginX + (col * (grid.CellWidth + grid.GapX));
        var y = grid.OriginY + (row * (grid.CellHeight + grid.GapY));
        var pivot = SpriteForgePivots.Normalize(sprite.Pivot ?? grid.DefaultPivot);
        var (pivotX, pivotY) = SpriteForgePivots.Resolve(pivot, x, y, grid.CellWidth, grid.CellHeight);
        return new SpriteForgeResolvedFrame
        {
            SpriteId = sprite.Id,
            AnimationId = animationId,
            FrameIndex = frameIndex,
            X = x,
            Y = y,
            Width = grid.CellWidth,
            Height = grid.CellHeight,
            OffsetX = sprite.OffsetX,
            OffsetY = sprite.OffsetY,
            Scale = sprite.Scale,
            Pivot = pivot,
            PivotX = pivotX,
            PivotY = pivotY,
            Source = SpriteForgeResolvedFrameSource.GridCell,
            GridId = gridId,
            Row = row,
            Col = col
        };
    }
}
