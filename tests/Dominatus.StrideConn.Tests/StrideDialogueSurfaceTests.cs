using System.Reflection;
using Ariadne.OptFlow.Commands;
using Stride.Engine;
using Stride.UI;

namespace Dominatus.StrideConn.Tests;

public sealed class StrideDialogueSurfaceTests
{
    [Fact]
    public void EnsureInitialized_AttachesUiComponentAndPage()
    {
        var entity = new Entity("dialogue");
        var surface = new StrideDialogueSurface(entity);

        surface.EnsureInitialized();

        var ui = entity.Get<UIComponent>();
        Assert.NotNull(ui);
        Assert.NotNull(ui!.Page);
        Assert.NotNull(ui.Page.RootElement);
    }

    [Fact]
    public void TryShowLine_MakesUiEnabled()
    {
        var entity = new Entity("dialogue");
        var surface = new StrideDialogueSurface(entity);
        surface.EnsureInitialized();

        var accepted = surface.TryShowLine(new DiagLineCommand("hello", "Narrator"), () => { });

        var ui = entity.Get<UIComponent>();
        Assert.True(accepted);
        Assert.NotNull(ui);
        Assert.True(ui!.Enabled);
    }

    [Fact]
    public void CompleteLine_HidesUi()
    {
        var entity = new Entity("dialogue");
        var surface = new StrideDialogueSurface(entity);
        surface.EnsureInitialized();
        surface.TryShowLine(new DiagLineCommand("hello", "Narrator"), () => { });

        var completeLine = typeof(StrideDialogueSurface).GetMethod("CompleteLine", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(completeLine);
        completeLine!.Invoke(surface, null);

        var ui = entity.Get<UIComponent>();
        Assert.NotNull(ui);
        Assert.False(ui!.Enabled);
    }
}
