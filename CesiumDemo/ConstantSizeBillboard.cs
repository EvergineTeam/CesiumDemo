using System;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Mathematics;

namespace CesiumDemo
{
    /// <summary>
    /// This Component scales the attached entity so it always appears the same size
    /// on screen, regardless of the distance to the camera.
    /// </summary>
    class ConstantSizeBillboard : Behavior
    {
        [BindComponent]
        private Transform3D transform;

        public float Scale = 1;

        protected override void Update(TimeSpan gameTime)
        {
            var camera = this.Managers.RenderManager.ActiveCamera3D;
            if (camera == null)
            {
                return;
            }
            var cameraPos = camera.Transform.Position;
            var billboardPos = this.transform.Position;
            float distance = (float)Vector3.Distance(cameraPos, billboardPos);
            float scale = distance * Scale;
            this.transform.Scale = new Vector3(scale);
        }
    }
}
