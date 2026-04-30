// CleanUiPostFxPatch.cs
// Drop this into your project (eg. Scripts/), add it as a StartupScript in your main scene.
// Goal: render UI after post effects so UI colors/text stay crisp.

using System;
using System.Linq;
using Stride.Core;
using Stride.Engine;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.UI;

namespace CleanUIScriptTest
{
    [DataContract]
    public class CleanUiPostFxPatch : StartupScript
    {
        /// <summary>
        /// If true, automatically moves every UIComponent in the active scene to RenderGroup31.
        /// </summary>
        public bool MoveAllUiToRenderGroup31 = true;

        /// <summary>
        /// If an entity has this tag, its UIComponent will NOT be moved to Group31.
        /// Handy for “world-space UI that should be postFX’d”.
        /// Leave empty to disable.
        /// </summary>
        /// 
        /// public string ExcludeTag = "KeepUiInMainPass";
        /// 
        /// Placeholder for implementation

        public override void Start()
        {
            var sceneSystem = Services.GetService<SceneSystem>();
            if (sceneSystem == null)
                return;

            var compositor = sceneSystem.GraphicsCompositor;
            if (compositor == null)
                return;

            // Idempotency: if already patched, bail.
            if (IsAlreadyPatched(compositor))
            {
                if (MoveAllUiToRenderGroup31)
                    MoveSceneUiToGroup31(sceneSystem.SceneInstance?.RootScene);
                return;
            }

            // 1) Ensure render stage exists
            var uiStage = GetOrCreateUiStage(compositor);

            // 2) Ensure UI render feature routes Group31 to UiStage
            AddUiStageSelectorForGroup31(compositor, uiStage);

            // 3) Patch GAME entry point: render everything except Group31, then render Group31 last
            PatchGameEntryPoint(compositor, uiStage);

            // 4) Patch EDITOR preview (best-effort): make editor preview use the patched game pipeline
            PatchEditorPreviewBestEffort(compositor);

            // 5) Move UI components to Group31 (optional, but makes this “one script and done”)
            if (MoveAllUiToRenderGroup31)
                MoveSceneUiToGroup31(sceneSystem.SceneInstance?.RootScene);
        }

        private static bool IsAlreadyPatched(GraphicsCompositor compositor)
        {
            // If we already have UiStage and a selector for Group31, assume patched.
            var hasStage = compositor.RenderStages.Any(s => string.Equals(s.Name, "UiStage", StringComparison.Ordinal));
            if (!hasStage) return false;

            var uiFeature = compositor.RenderFeatures.OfType<UIRenderFeature>().FirstOrDefault();
            if (uiFeature == null) return false;

            var hasSelector = uiFeature.RenderStageSelectors
                .OfType<SimpleGroupToRenderStageSelector>()
                .Any(sel => sel.RenderGroup == RenderGroupMask.Group31 &&
                            sel.RenderStage != null &&
                            string.Equals(sel.RenderStage.Name, "UiStage", StringComparison.Ordinal));

            return hasSelector;
        }

        private static RenderStage GetOrCreateUiStage(GraphicsCompositor compositor)
        {
            var existing = compositor.RenderStages.FirstOrDefault(s => string.Equals(s.Name, "UiStage", StringComparison.Ordinal));
            if (existing != null)
                return existing;

            // Try to copy from existing UI stage if present
            var defaultUiStage = compositor.RenderStages
                .FirstOrDefault(s => string.Equals(s.Name, "UIRenderStage", StringComparison.Ordinal))
                ?? compositor.RenderStages.FirstOrDefault(s => s.Name.Contains("UI", StringComparison.OrdinalIgnoreCase));

            var effectSlotName = defaultUiStage?.EffectSlotName ?? "UI";

            var stage = new RenderStage("UiStage", effectSlotName);

            // Copy sort mode if we found a UI stage; otherwise leave default (avoids enum mismatch)
            if (defaultUiStage != null)
                stage.SortMode = defaultUiStage.SortMode;

            compositor.RenderStages.Add(stage);
            return stage;
        }

        private static void AddUiStageSelectorForGroup31(GraphicsCompositor compositor, RenderStage uiStage)
        {
            var uiFeature = compositor.RenderFeatures.OfType<UIRenderFeature>().FirstOrDefault();
            if (uiFeature == null)
                return;

            // Avoid duplicates
            var already = uiFeature.RenderStageSelectors
                .OfType<SimpleGroupToRenderStageSelector>()
                .Any(sel => sel.RenderGroup == RenderGroupMask.Group31 && ReferenceEquals(sel.RenderStage, uiStage));

            if (already)
                return;

            uiFeature.RenderStageSelectors.Add(new SimpleGroupToRenderStageSelector
            {
                RenderGroup = RenderGroupMask.Group31,
                RenderStage = uiStage,
            });
        }

        private static void PatchGameEntryPoint(GraphicsCompositor compositor, RenderStage uiStage)
        {
            // We’ll build:
            //   SceneRendererCollection
            //     [0] main camera renderer (RenderMask = All but Group31) -> your original pipeline
            //     [1] UI camera renderer  (RenderMask = Group31) -> SingleStageRenderer(UiStage)

            var originalGame = compositor.Game;
            if (originalGame == null)
                return;

            // Pick a camera slot (usually the first is Main)
            var cameraSlot = compositor.Cameras.FirstOrDefault();
            if (cameraSlot == null)
                return;

            // Main pass: keep original pipeline, but ensure it doesn't draw Group31
            SceneCameraRenderer mainCameraRenderer;
            if (originalGame is SceneCameraRenderer scr)
            {
                mainCameraRenderer = scr;
                mainCameraRenderer.RenderMask = RenderGroupMask.All & ~RenderGroupMask.Group31;
            }
            else
            {
                mainCameraRenderer = new SceneCameraRenderer
                {
                    Camera = cameraSlot,
                    RenderMask = RenderGroupMask.All & ~RenderGroupMask.Group31,
                    Child = originalGame
                };
            }

            // UI pass: draw only Group31, and only the UI stage, after postFX
            var uiCameraRenderer = new SceneCameraRenderer
            {
                Camera = cameraSlot,
                RenderMask = RenderGroupMask.Group31,
                Child = new SingleStageRenderer
                {
                    RenderStage = uiStage
                }
            };

            var collection = new SceneRendererCollection();
            collection.Children.Add(mainCameraRenderer);
            collection.Children.Add(uiCameraRenderer);

            compositor.Game = collection;
        }

        private static void PatchEditorPreviewBestEffort(GraphicsCompositor compositor)
        {
            // In many setups, compositor.Editor is an EditorTopLevelCompositor.
            // It can preview the game renderer via EnablePreviewGame + PreviewGame.
            if (compositor.Editor is EditorTopLevelCompositor editorTopLevel)
            {
                editorTopLevel.EnablePreviewGame = true;
                editorTopLevel.PreviewGame = compositor.Game;
            }
        }

        private static void MoveSceneUiToGroup31(Scene rootScene)
        {
            if (rootScene == null)
                return;

            foreach (var entity in rootScene.Entities)
                MoveUiRecursive(entity);
        }

        private static void MoveUiRecursive(Entity entity)
        {
            if (entity == null)
                return;

            var ui = entity.Get<UIComponent>();
            ui?.RenderGroup = RenderGroup.Group31;

            var transform = entity.Transform;
            if (transform != null)
            {
                foreach (var child in transform.Children)
                    MoveUiRecursive(child.Entity);
            }
        }
    }
}
