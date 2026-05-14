using System;
using Evergine.Bindings.CesiumNative;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Mathematics;

namespace CesiumDemo
{
    /// <summary>
    /// This is a helper component for positioning an entity on the Earth, given latitude-longitude-height coordinates
    /// </summary>
    public class CesiumPlacerComponent : Component
    {
        [BindComponent]
        private Transform3D transform;

        private Ellipsoid ellipsoid = Ellipsoid.Wgs84();
        private double latitude = 0; // in radians
        private double longitude = 0; // in radians
        private double height = 0;
        private bool aboveTerrain = false; // if true, height is relative to the terrain, otherwise it's relative to the ellipsoid
        private Quaternion rotation = Quaternion.Identity;

        public double Latitude
        {
            get => latitude;
            set
            {
                this.latitude = value;
                this.UpdateTransform();
            }
        }

        public double Longitude
        {
            get => longitude;
            set
            {
                this.longitude = value;
                this.UpdateTransform();
            }
        }

        public double Height
        {
            get => height;
            set
            {
                this.height = value;
                this.UpdateTransform();
            }
        }

        public bool AboveTerrain
        {
            get => aboveTerrain;
            set
            {
                this.aboveTerrain = value;
                this.UpdateTransform();
            }
        }

        public Quaternion Rotation
        {
            get => rotation;
            set
            {
                this.rotation = value;
                this.UpdateTransform();
            }
        }

        protected override void OnActivated()
        {
            base.OnActivated();
            this.UpdateTransform();
        }

        public void UpdateTransform()
        {
            if (this.transform != null)
            {
                Vec3 cartesianPos = ellipsoid.CartographicToCartesian(new Cartographic()
                {
                    Latitude = this.latitude,
                    Longitude = this.longitude,
                    Height = this.height
                });
                this.transform.LocalPosition = new Vector3((float)cartesianPos.X, (float)cartesianPos.Z, -(float)cartesianPos.Y);
            }
        }
    }
}
