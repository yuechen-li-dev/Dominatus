using Dominatus.Assets.Toml;

namespace Dominatus.SpriteForge.Tests;

public sealed class SpriteForgeTomlLoaderTests
{
    [Fact]
    public void SpriteForgeLoader_LoadsNestedGridToml()
    {
        var path = WriteTempToml("""
            [atlas]
            image = "atlas.png"
            width = 480
            height = 240

            [grids.villagers_down]
            origin_x = 0
            origin_y = 0
            columns = 3
            rows = 2
            cell_width = 80
            cell_height = 120
            default_pivot = "bottom_center"

            [grids.props]
            origin_x = 240
            origin_y = 0
            columns = 3
            rows = 2
            cell_width = 80
            cell_height = 120
            default_pivot = "bottom_center"

            [sprites.maya]
            kind = "villager"
            display_name = "Maya"

            [sprites.maya.animations.down]
            grid = "villagers_down"
            row = 0
            frames = [0, 1, 2]
            fps = 6
            loop = true
            """);

        try
        {
            var result = SpriteForgeTomlLoader.LoadFile(path);

            Assert.True(result.Success, AssetDiagnosticFormatter.FormatMany(result.Diagnostics));
            var atlas = Assert.IsType<SpriteForgeAtlas>(result.Atlas);
            Assert.Equal("atlas.png", atlas.Image);
            Assert.Equal(2, atlas.Grids.Count);
            Assert.Single(atlas.Sprites);
            Assert.Equal(3, atlas.Sprites["maya"].Animations["down"].Frames.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SpriteForgeValidator_RejectsGridOutOfBounds()
    {
        var path = WriteTempToml("""
            [atlas]
            image = "atlas.png"
            width = 100
            height = 100

            [grids.bad]
            origin_x = 20
            origin_y = 20
            columns = 2
            rows = 2
            cell_width = 50
            cell_height = 50
            """);

        try
        {
            var result = SpriteForgeTomlLoader.LoadFile(path);

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, d => d.Code == "spriteforge.grid_out_of_bounds");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SpriteForgeValidator_RejectsUnknownAnimationGrid()
    {
        var path = WriteTempToml("""
            [atlas]
            image = "atlas.png"
            width = 100
            height = 100

            [grids.good]
            origin_x = 0
            origin_y = 0
            columns = 2
            rows = 2
            cell_width = 50
            cell_height = 50

            [sprites.maya]
            kind = "villager"

            [sprites.maya.animations.down]
            grid = "missing"
            row = 0
            frames = [0, 1]
            """);

        try
        {
            var result = SpriteForgeTomlLoader.LoadFile(path);

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, d => d.Code == "spriteforge.unknown_animation_grid");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SpriteForgeResolver_ResolvesGridFrameToAbsoluteRect()
    {
        var atlas = new SpriteForgeAtlas
        {
            SourcePath = "inline",
            Image = "atlas.png",
            ResolvedImagePath = "atlas.png",
            Width = 480,
            Height = 240,
            Grids = new Dictionary<string, SpriteForgeGrid>(StringComparer.Ordinal)
            {
                ["villagers_down"] = new()
                {
                    Id = "villagers_down",
                    OriginX = 0,
                    OriginY = 0,
                    Columns = 3,
                    Rows = 2,
                    CellWidth = 80,
                    CellHeight = 120,
                    DefaultPivot = SpriteForgePivots.BottomCenter
                }
            },
            Sprites = new Dictionary<string, SpriteForgeSprite>(StringComparer.Ordinal)
            {
                ["maya"] = new()
                {
                    Id = "maya",
                    Kind = "villager",
                    Animations = new Dictionary<string, SpriteForgeAnimation>(StringComparer.Ordinal)
                    {
                        ["down"] = new()
                        {
                            Id = "down",
                            Grid = "villagers_down",
                            Row = 1,
                            Frames =
                            [
                                new SpriteForgeFrameRef { Col = 2 }
                            ]
                        }
                    }
                }
            }
        };

        var resolved = new SpriteForgeResolver().ResolveAnimation(atlas, "maya", "down");

        var frame = Assert.Single(resolved);
        Assert.Equal(160, frame.X);
        Assert.Equal(120, frame.Y);
        Assert.Equal(80, frame.Width);
        Assert.Equal(120, frame.Height);
        Assert.Equal(SpriteForgeResolvedFrameSource.GridCell, frame.Source);
        Assert.Equal(SpriteForgePivots.BottomCenter, frame.Pivot);
    }

    [Fact]
    public void SpriteForgeResolver_ResolvesAbsoluteFrameOverride()
    {
        var atlas = new SpriteForgeAtlas
        {
            SourcePath = "inline",
            Image = "atlas.png",
            ResolvedImagePath = "atlas.png",
            Width = 480,
            Height = 240,
            Grids = new Dictionary<string, SpriteForgeGrid>(StringComparer.Ordinal),
            Frames = new Dictionary<string, SpriteForgeFrame>(StringComparer.Ordinal)
            {
                ["maya.down.idle_exact"] = new()
                {
                    Id = "maya.down.idle_exact",
                    X = 24,
                    Y = 8,
                    Width = 72,
                    Height = 104,
                    Pivot = SpriteForgePivots.BottomCenter,
                    OffsetY = -4
                }
            },
            Sprites = new Dictionary<string, SpriteForgeSprite>(StringComparer.Ordinal)
            {
                ["maya"] = new()
                {
                    Id = "maya",
                    Kind = "villager",
                    OffsetX = 3,
                    Animations = new Dictionary<string, SpriteForgeAnimation>(StringComparer.Ordinal)
                    {
                        ["down_exact"] = new()
                        {
                            Id = "down_exact",
                            Frames =
                            [
                                new SpriteForgeFrameRef { Frame = "maya.down.idle_exact" }
                            ]
                        }
                    }
                }
            }
        };

        var resolved = new SpriteForgeResolver().ResolveAnimation(atlas, "maya", "down_exact");

        var frame = Assert.Single(resolved);
        Assert.Equal(24, frame.X);
        Assert.Equal(8, frame.Y);
        Assert.Equal(72, frame.Width);
        Assert.Equal(104, frame.Height);
        Assert.Equal(-4, frame.OffsetY);
        Assert.Equal(3, frame.OffsetX);
        Assert.Equal(SpriteForgeResolvedFrameSource.AbsoluteFrame, frame.Source);
    }

    [Fact]
    public void SpriteForgeValidator_RejectsFrameIndexOutOfBounds()
    {
        var path = WriteTempToml("""
            [atlas]
            image = "atlas.png"
            width = 120
            height = 120

            [grids.good]
            origin_x = 0
            origin_y = 0
            columns = 2
            rows = 1
            cell_width = 60
            cell_height = 120

            [sprites.maya]
            kind = "villager"

            [sprites.maya.animations.down]
            grid = "good"
            row = 0
            frames = [2]
            """);

        try
        {
            var result = SpriteForgeTomlLoader.LoadFile(path);

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, d => d.Code == "spriteforge.frame_index_out_of_bounds");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SpriteForgeValidator_RejectsDuplicateOrInvalidIds()
    {
        var path = WriteTempToml("""
            [atlas]
            image = "atlas.png"
            width = 120
            height = 120

            [grids."bad id"]
            origin_x = 0
            origin_y = 0
            columns = 1
            rows = 1
            cell_width = 120
            cell_height = 120
            """);

        try
        {
            var result = SpriteForgeTomlLoader.LoadFile(path);

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, d => d.Code == "spriteforge.invalid_grid_id");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SpriteForge_TinyTownToml_Loads()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var path = Path.Combine(directory, "tinytown_sprite_alpha.spriteforge.toml");
        var result = SpriteForgeTomlLoader.LoadFile(path, new SpriteForgeLoadOptions { RequireImageFileExists = true });

        Assert.True(result.Success, AssetDiagnosticFormatter.FormatMany(result.Diagnostics));
        var atlas = Assert.IsType<SpriteForgeAtlas>(result.Atlas);
        Assert.Equal("tinytown_sprite_alpha.png", atlas.Image);
        Assert.True(atlas.Grids.ContainsKey("villagers_down"));
        Assert.True(atlas.Sprites.ContainsKey("maya"));
        Assert.True(atlas.Frames.ContainsKey("maya.down.idle_exact"));
    }

    private static string WriteTempToml(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"spriteforge-{Guid.NewGuid():N}.sprite.toml");
        File.WriteAllText(path, content.Replace("\n", Environment.NewLine));
        return path;
    }
}
