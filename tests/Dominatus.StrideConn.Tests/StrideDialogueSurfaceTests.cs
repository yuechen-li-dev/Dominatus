using System.Reflection;
using Ariadne.OptFlow.Commands;
using Stride.Engine;
using Stride.UI;
using Stride.UI.Controls;

namespace Dominatus.StrideConn.Tests;

public sealed class StrideDialogueSurfaceTests
{
    [Fact]
    public void StrideDialogueSurface_EnsureInitialized_CreatesRenderableUiPage()
    {
        var entity = new Entity("dialogue");
        var surface = new StrideDialogueSurface(entity);

        surface.EnsureInitialized();

        var ui = ResolveUiComponent(entity);
        Assert.NotNull(ui);
        Assert.True(ui!.Enabled);
        Assert.NotNull(ui.Page);
        Assert.NotNull(ui.Page.RootElement);
    }

    [Fact]
    public void StrideDialogueSurface_CanUseExistingUiComponent()
    {
        var entity = new Entity("dialogue");
        var existingUi = new UIComponent();
        entity.Components.Add(existingUi);

        var surface = new StrideDialogueSurface(entity, existingUi);
        surface.EnsureInitialized();

        Assert.Same(existingUi, entity.Get<UIComponent>());
        Assert.NotNull(existingUi.Page);
        Assert.NotNull(existingUi.Page!.RootElement);
    }


    [Fact]
    public void StrideDialogueSurface_NoScene_FallsBackToEntityUiComponent()
    {
        var entity = new Entity("dialogue");
        var surface = new StrideDialogueSurface(entity);

        surface.EnsureInitialized();

        var ui = entity.Get<UIComponent>();
        Assert.NotNull(ui);
        Assert.NotNull(ui!.Page);
    }

    [Fact]
    public void StrideDialogueSurface_StatusTextVisibleAfterInitialize()
    {
        var entity = new Entity("dialogue");
        var surface = new StrideDialogueSurface(entity);

        surface.EnsureInitialized();

        var statusField = typeof(StrideDialogueSurface).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(statusField);
        var status = statusField!.GetValue(surface) as TextBlock;

        Assert.NotNull(status);
        Assert.Equal("Dominatus installer started", status!.Text);
        Assert.Equal(Visibility.Visible, status.Visibility);
    }

    [Fact]
    public void TryShowLine_MakesDialogueVisible()
    {
        var entity = new Entity("dialogue");
        var surface = new StrideDialogueSurface(entity);
        surface.EnsureInitialized();

        var accepted = surface.TryShowLine(new DiagLineCommand("hello", "Narrator"), () => { });

        var ui = ResolveUiComponent(entity);
        var panelField = typeof(StrideDialogueSurface).GetField("_dialoguePanel", BindingFlags.Instance | BindingFlags.NonPublic);
        var panel = panelField?.GetValue(surface) as Stride.UI.Panels.StackPanel;

        Assert.True(accepted);
        Assert.NotNull(ui);
        Assert.True(ui!.Enabled);
        Assert.NotNull(panel);
        Assert.Equal(Visibility.Visible, panel!.Visibility);
    }

    [Fact]
    public void CompleteLine_HidesDialoguePanel()
    {
        var entity = new Entity("dialogue");
        var surface = new StrideDialogueSurface(entity);
        surface.EnsureInitialized();
        surface.TryShowLine(new DiagLineCommand("hello", "Narrator"), () => { });

        var completeLine = typeof(StrideDialogueSurface).GetMethod("CompleteLine", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(completeLine);
        completeLine!.Invoke(surface, null);

        var panelField = typeof(StrideDialogueSurface).GetField("_dialoguePanel", BindingFlags.Instance | BindingFlags.NonPublic);
        var panel = panelField?.GetValue(surface) as Stride.UI.Panels.StackPanel;

        Assert.NotNull(panel);
        Assert.Equal(Visibility.Collapsed, panel!.Visibility);
    }

    private static UIComponent? ResolveUiComponent(Entity entity)
    {
        var ui = entity.Get<UIComponent>();
        if (ui is not null)
            return ui;

        foreach (var childTransform in entity.Transform.Children)
        {
            var childUi = childTransform.Entity.Get<UIComponent>();
            if (childUi is not null)
                return childUi;
        }

        return null;
    }
}
