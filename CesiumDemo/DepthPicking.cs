using Evergine.Bindings.CesiumNative;
using Evergine.Common.Graphics;
using Evergine.Common.Input.Mouse;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Framework.Managers;
using Evergine.Framework.Services;
using Evergine.Mathematics;
using System;

namespace CesiumDemo
{
    /// <summary>
    /// Helper class that uses the depth-buffer to compute the position of a pixel
    /// </summary>
    class DepthPicking
    {
        private GraphicsPresenter graphicsPresenter;
        private GraphicsContext graphicsContext;
        private BaseRenderManager renderManager;
        private Texture stagingTexture;

        public DepthPicking(BaseRenderManager renderManager)
        {
            this.renderManager = renderManager;
            this.graphicsPresenter = Application.Current.Container.Resolve<GraphicsPresenter>();
            this.graphicsContext = Application.Current.Container.Resolve<GraphicsContext>();
        }

        public bool Pick(Point mousePixelPosition, out Vector3 viewSpacePos, out Vector3 worldSpacePos)
        {
            viewSpacePos = Vector3.Zero;
            worldSpacePos = Vector3.Zero;

            var camera = this.renderManager.ActiveCamera3D;
            var drawContext = camera.DrawContext;
            var intermediateFrameBuffer = drawContext.IntermediateFrameBuffer;
            var depthTarget = intermediateFrameBuffer?.DepthStencilTarget;
            var depthTexture = depthTarget.Value.Texture;
            var desc = depthTexture.Description;
            int mx = mousePixelPosition.X;
            int my = mousePixelPosition.Y;

            if (!depthTarget.HasValue ||
                mx < 0 || mx >= desc.Width || my < 0 || my >= desc.Height)
            {
                return false;
            }

            // Create or recreate staging texture if needed
            if (stagingTexture == null ||
                stagingTexture.Description.Width != desc.Width ||
                stagingTexture.Description.Height != desc.Height)
            {
                if (stagingTexture != null)
                {
                    stagingTexture.Dispose();
                }

                var stagingDesc = new TextureDescription
                {
                    Type = TextureType.Texture2D,
                    Format = desc.Format,
                    Width = desc.Width,
                    Height = desc.Height,
                    Depth = 1,
                    ArraySize = 1,
                    MipLevels = 1,
                    SampleCount = TextureSampleCount.None,
                    Usage = ResourceUsage.Staging,
                    CpuAccess = ResourceCpuAccess.Read,
                    Flags = TextureFlags.None,
                };

                stagingTexture = graphicsContext.Factory.CreateTexture(ref stagingDesc, "DepthPicking_Staging");
            }

            // Copy the single pixel from the depth buffer to the staging texture
            var commandQueue = this.graphicsPresenter.GraphicsCommandQueue;
            var commandBuffer = commandQueue.CommandBuffer();
            commandBuffer.Begin();
            commandBuffer.CopyTextureDataTo(
                depthTexture, 0, 0, 0, 0, 0,
                stagingTexture, 0, 0, 0, 0, 0,
                desc.Width, desc.Height, 1, 1); // I wish we could just copy the single pixel that we pick, but there is a limitation in DX11 that, for depth-stencil textures, you must copy the whole resource
            commandBuffer.End();
            commandBuffer.Commit();
            commandQueue.Submit();
            commandQueue.WaitIdle();

            // Read back the depth value
            var mappedResource = graphicsContext.MapMemory(stagingTexture, MapMode.Read);
            float rawDepth;
            unsafe
            {
                int pixelIndex = mx + my * (int)desc.Width;
                if (desc.Format == PixelFormat.D32_Float || desc.Format == PixelFormat.D32_Float_S8X24_UInt)
                {
                    var pixels = (float*)mappedResource.Data;
                    int skipStencil = desc.Format == PixelFormat.D32_Float_S8X24_UInt ? 2 : 1; // in case we have stencil, we must skip odd floats because depth values and stencil values are interleaved
                    rawDepth = pixels[skipStencil * pixelIndex];
                }
                else
                {
                    // D24 normalized: read as uint24 and normalize
                    var pixels = (uint*)mappedResource.Data;
                    uint depthBits = pixels[pixelIndex] & 0x00FFFFFF;
                    rawDepth = depthBits / (float)0x00FFFFFF;
                }
            }
            graphicsContext.UnmapMemory(stagingTexture);

            var canonicalPos4 = new Vector4(
                (2.0f * mx) / desc.Width - 1.0f,
                1.0f - (2.0f * my) / desc.Height,
                rawDepth,
                1.0f);

            var viewSpacePos4 = Vector4.Transform(canonicalPos4, camera.RenderProjectionInverse);
            var worldSpacePos4 = Vector4.Transform(canonicalPos4, camera.RenderViewProjectionInverse);
            viewSpacePos = viewSpacePos4.ToVector3() / viewSpacePos4.W;
            worldSpacePos = worldSpacePos4.ToVector3() / worldSpacePos4.W;

            return true;
        }
    }
}