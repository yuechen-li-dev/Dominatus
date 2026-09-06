using Dominatus.SpriteForge;
using Xunit;

namespace Dominatus.SpriteForge.Tests;

public sealed class SpriteForgeUiNineSliceTests
{
    [Fact]
    public void LoaderBuildsValidatedUiPanelMetadata()
    {
        string path = WriteToml("""
            [atlas]
            image = "ui.png"
            width = 128
            height = 64

            [ui_panels.dialogue]
            x = 0
            y = 0
            width = 80
            height = 64
            left = 8
            top = 8
            right = 8
            bottom = 8
            edge_mode = "tile"
            center_mode = "stretch"
            border_scale = 0.5
            extrusion = 1
            """);
        try
        {
            SpriteForgeLoadResult result = SpriteForgeTomlLoader.LoadFile(path);

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
            SpriteForgeNineSlicePanel panel = Assert.Single(result.Atlas!.UiPanels).Value;
            Assert.Equal("dialogue", panel.Id);
            Assert.Equal(SpriteForgeTileMode.Tile, panel.EdgeMode);
            Assert.Equal(SpriteForgeTileMode.Stretch, panel.CenterMode);
            Assert.Equal(0.5f, panel.BorderScale);
            Assert.Equal(1, panel.Extrusion);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(20, 4, "spriteforge.ui_margins_exceed_source")]
    [InlineData(-1, 4, "spriteforge.ui_margin_negative")]
    public void LoaderRejectsInvalidSliceMargins(int left, int right, string code)
    {
        string path = WriteToml($"""
            [atlas]
            image = "ui.png"
            width = 16
            height = 16

            [ui_panels.panel]
            x = 0
            y = 0
            width = 16
            height = 16
            left = {left}
            top = 2
            right = {right}
            bottom = 2
            edge_mode = "tile"
            center_mode = "tile"
            """);
        try
        {
            SpriteForgeLoadResult result = SpriteForgeTomlLoader.LoadFile(path);

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, item => item.Code == code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoaderRejectsNonPositiveBorderScale()
    {
        string path = WriteToml("""
            [atlas]
            image = "ui.png"
            width = 16
            height = 16

            [ui_panels.panel]
            x = 0
            y = 0
            width = 16
            height = 16
            left = 2
            top = 2
            right = 2
            bottom = 2
            edge_mode = "stretch"
            center_mode = "stretch"
            border_scale = 0
            """);
        try
        {
            SpriteForgeLoadResult result = SpriteForgeTomlLoader.LoadFile(path);

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, item => item.Code == "spriteforge.ui_border_scale_invalid");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteToml(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"spriteforge-ui-{Guid.NewGuid():N}.toml");
        File.WriteAllText(path, content);
        return path;
    }
}
