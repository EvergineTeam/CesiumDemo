using Evergine.Bindings.Imgui;
using Evergine.Cesium;
using Evergine.Cesium.Components;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Framework.Services;
using Evergine.Mathematics;
using Evergine.UI;
using System;

namespace CesiumDemo
{
    public class MyScene : Scene
    {
        private CesiumCoordinator cesiumCoordinator = null;

        public override void RegisterManagers()
        {
            base.RegisterManagers();

            this.Managers.AddManager(new ImGuiManager()
            {
                ImGuizmoEnabled = false,
                ImPlotEnabled = false,
                ImNodesEnabled = false,
                MergeCustomFonts = false,
            });
        }

        protected override void CreateScene()
        {
            var uiController = new Entity().AddComponent(new UIController());
            this.Managers.EntityManager.Add(uiController);

            var cesiumLoader = new CesiumCoordinator()
            {
                AccessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJqdGkiOiI5YTY4ODNmMy1hMmRiLTQyMWUtOWRmNS0yZDI4MmEyNTU1ZjciLCJpZCI6NDI3MzkxLCJpc3MiOiJodHRwczovL2lvbi5jZXNpdW0uY29tIiwiYXVkIjoidW5kZWZpbmVkX2RlZmF1bHQiLCJpYXQiOjE3Nzc5NzMxNDN9.HWGLvsijjIVhvsqdWiQ8JNdWqqA72tZ8n5gx0p-wPwc",
                EntityManager = this.Managers.EntityManager,

            };

            this.Managers.AddManager(cesiumLoader);
        }

        protected override void Update(TimeSpan deltaTime)
        {
            base.Update(deltaTime);
            cesiumCoordinator?.Update(deltaTime);
        }

        private void startMenu()
        {
            int screenWidth = this.
            ImguiNative.igSetNextWindowPos(new Vector2(12, 12), ImGuiCond.FirstUseEver, Vector2.Zero);
            ImguiNative.igSetNextWindowSize(new Vector2(400, 400), ImGuiCond.FirstUseEver);
            ImguiNative.igBegin("Cesium", null, (ImGuiWindowFlags)0);

            ImguiNative.igEnd();
        }
    }
}


