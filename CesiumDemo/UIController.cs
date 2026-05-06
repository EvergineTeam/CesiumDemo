using System;
using Evergine.Bindings.Imgui;
using Evergine.Framework;
using Evergine.Mathematics;

namespace CesiumDemo
{
    public class UIController : Behavior
    {
        protected override unsafe bool OnAttached()
        {
            return base.OnAttached();
        }

        protected override unsafe void Update(TimeSpan gameTime)
        {
            ImguiNative.igSetNextWindowPos(new Vector2(12, 12), ImGuiCond.FirstUseEver, Vector2.Zero);
            ImguiNative.igSetNextWindowSize(new Vector2(400, 400), ImGuiCond.FirstUseEver);
            ImguiNative.igBegin("Cesium", null, (ImGuiWindowFlags)0);
            
            ImguiNative.igEnd();
        }
    }
}
