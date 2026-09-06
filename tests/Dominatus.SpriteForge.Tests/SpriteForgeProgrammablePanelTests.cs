namespace Dominatus.SpriteForge.Tests;

public sealed class SpriteForgeProgrammablePanelTests
{
    [Fact]
    public void Generated_obj_ts_projection_loads_regions_and_programmable_edges()
    {
        string root = Path.Combine(Path.GetTempPath(), "spriteforge-programmable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string imagePath = Path.Combine(root, "atlas.png");
            File.WriteAllBytes(imagePath, [0]);
            string path = Path.Combine(root, "panel.runtime.toml");
            File.WriteAllText(path, """
                schema_version = 1
                asset_id = "example"

                [atlas]
                image = "atlas.png"
                width = 64
                height = 64
                source_kind = "generated-obj-ts"

                [regions."corner"]
                x = 0
                y = 0
                width = 8
                height = 8

                [regions."rail"]
                x = 8
                y = 0
                width = 16
                height = 8

                [regions."center"]
                x = 8
                y = 8
                width = 16
                height = 16

                [programmable_panels."panel"]
                top_left = "corner"
                top_right = "corner"
                bottom_right = "corner"
                bottom_left = "corner"
                center_policy = "stretch-region"
                center_region = "center"
                border_scale = 1.0
                minimum_width = 24
                minimum_height = 24

                [[programmable_panels."panel".top]]
                id = "top.rail"
                region = "rail"
                allocation = "flex"
                length = 8
                weight = 1
                sampling = "stretch"

                [[programmable_panels."panel".right]]
                id = "right.rail"
                region = "rail"
                allocation = "flex"
                length = 8
                weight = 1
                sampling = "tile"

                [[programmable_panels."panel".bottom]]
                id = "bottom.rail"
                region = "rail"
                allocation = "fixed"
                length = 16
                weight = 0
                sampling = "crop"

                [[programmable_panels."panel".left]]
                id = "left.rail"
                region = "rail"
                allocation = "flex"
                length = 8
                weight = 1
                sampling = "stretch"
                """);

            SpriteForgeLoadResult result = SpriteForgeTomlLoader.LoadFile(path);

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
            SpriteForgeAtlas atlas = result.Atlas!;
            Assert.Equal(SpriteForgeAssetAuthoringKind.GeneratedObjectTypeScript, atlas.AuthoringKind);
            Assert.Equal(3, atlas.Regions.Count);
            SpriteForgeProgrammablePanel panel = Assert.Single(atlas.ProgrammablePanels).Value;
            Assert.Equal(SpriteForgeSamplingMode.Tile, Assert.Single(panel.Right.Segments).Sampling);
            Assert.Equal(SpriteForgeAllocationKind.Fixed, Assert.Single(panel.Bottom.Segments).Allocation);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
