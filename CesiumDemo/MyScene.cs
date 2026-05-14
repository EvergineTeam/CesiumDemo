using Evergine.Bindings.CesiumNative;
using Evergine.Bindings.Imgui;
using Evergine.Cesium;
using Evergine.Cesium.Components;
using Evergine.Cesium.Utils;
using Evergine.Common.Graphics;
using Evergine.Common.Input.Mouse;
using Evergine.Framework;
using Evergine.Framework.Assets.AssetParts;
using Evergine.Framework.Graphics;
using Evergine.Framework.Graphics.Effects;
using Evergine.Framework.Graphics.Materials;
using Evergine.Framework.Managers;
using Evergine.Framework.Services;
using Evergine.Mathematics;
using Evergine.Runtimes.GLB;
using Evergine.UI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using static Evergine.Bindings.Draco.Draco;

namespace CesiumDemo
{
    public class MyScene : Scene
    {
        private static readonly string saveAccessTokenFileName_cesium = "cesium_access_token.txt";
        private static readonly string saveAccessTokenFileName_azure = "azure_access_token.txt";
        private byte[] accessTokenBuffer = new byte[512];
        private byte[] inputTextBuffer = new byte[512];

        private GraphicsPresenter graphicsPresenter;
        private AssetsService assetsService;
        private CesiumCoordinator cesiumCoordinator = null;
        private DepthPicking depthPicking;
        private Model pinModel = null;
        private List<Entity> placedPins = new List<Entity>();
        private List<GeocodingAutocompleteSuggestion> geocodingSuggestions = new List<GeocodingAutocompleteSuggestion>();

        private unsafe bool imguiWantsCaptureMouse() => ImguiNative.igGetIO_Nil()->WantCaptureMouse == 1;

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

            //this.Managers.AddManager(new SceneTreeImgui());
        }

        protected async override void CreateScene()
        {
            this.LoadSavedAccessToken(saveAccessTokenFileName_cesium);
            this.assetsService = Application.Current.Container.Resolve<AssetsService>();
            this.pinModel = await GLBRuntime.Instance.Read("Meshes/pin.glb", (Evergine.Framework.Runtimes.MaterialData materialData) =>
            {
                return Task.Run(() =>
                {
                    var effect = this.assetsService.Load<Effect>(EvergineContent.Effects.StandardEffect);
                    var decorator = new StandardMaterial(effect)
                    {
                        BaseColor = Color.Red,
                        
                    };
                    return decorator.Material;
                });
            });
        }

        private static unsafe string u8PtrToString(byte* bufferPtr)
        {
            return System.Runtime.InteropServices.Marshal.PtrToStringUTF8((IntPtr)bufferPtr);
        }

        private static unsafe string u8ArrayToString(byte[] buffer)
        {
            fixed (byte* bufferPtr = buffer)
            {
                return u8PtrToString(bufferPtr);
            }
        }

        private string getAccessTokenStr() => u8ArrayToString(accessTokenBuffer);
        private string getInputTextStr() => u8ArrayToString(inputTextBuffer);

        private void LoadSavedAccessToken(string filePath)
        {
            var str = System.IO.File.Exists(filePath) ? System.IO.File.ReadAllText(filePath) : string.Empty;
            for (int i = 0; i < str.Length && i < accessTokenBuffer.Length; i++)
            {
                accessTokenBuffer[i] = (byte)str[i];
            }
            accessTokenBuffer[Math.Min(str.Length, accessTokenBuffer.Length - 1)] = 0; // null terminator
        }

        private void SaveAccessToken(string filePath)
        {
            System.IO.File.WriteAllText(filePath, this.getAccessTokenStr());
        }

        private void CreateCesiumCoordinator(string accessToken)
        {
            var cesiumLoader = new CesiumCoordinator()
            {
                AccessToken = accessToken,
                EntityManager = this.Managers.EntityManager,
            };

            this.Managers.AddManager(cesiumLoader);
            this.cesiumCoordinator = cesiumLoader;
        }

        protected override void Start()
        {
            base.Start();
            this.graphicsPresenter = Application.Current.Container.Resolve<GraphicsPresenter>();
        }

        protected override void Update(TimeSpan deltaTime)
        {
            base.Update(deltaTime);

            this.cesiumCoordinator?.WorldCamera.UIHasFocus = imguiWantsCaptureMouse();
        }

        protected override void Draw(TimeSpan deltaTime)
        {
            base.Draw(deltaTime);

            var lines = (this.Managers.RenderManager as RenderManager).LineBatch3D;
            foreach (var pinEntity in this.placedPins)
            {
                var transform = pinEntity.FindComponent<Transform3D>();
                var box = new BoundingOrientedBox(transform.Position, transform.Scale, transform.Orientation);
                lines.DrawBoundingOrientedBox(box, Color.Red);
            }

            if (this.cesiumCoordinator == null ||
                this.cesiumCoordinator.CurrentStatus == Status.Uninitialized ||
                this.cesiumCoordinator.CurrentStatus == Status.AwaitingConnection ||
                this.cesiumCoordinator.CurrentStatus == Status.CantAuthenticate ||
                this.cesiumCoordinator.CurrentStatus == Status.UnknownError ||
                this.cesiumCoordinator.CurrentStatus == Status.MissingTokenPermissions ||
                this.cesiumCoordinator.CurrentStatus == Status.CantReachEndpoint)
            {
                gui_startMenu();
            }
            else
            {
                gui_mainMenu();
                cesiumCoordinator?.Update(deltaTime);
            }
        }

        private (float, float) GetWindowSize()
        {
            var activeCamera = this.Managers.RenderManager.ActiveCamera3D;
            if (activeCamera != null)
            {
                return (activeCamera.ScreenViewport.Width, activeCamera.ScreenViewport.Height);
            }

            return (0, 0);
        }

        private unsafe void gui_startMenu()
        {
            (float screenWidth, float screenHeight) = this.GetWindowSize();
            float windowWidth = 500, windowHeight = 500;

            ImguiNative.igSetNextWindowPos(new Vector2(screenWidth, screenHeight) * 0.5f, ImGuiCond.Once, new Vector2(0.5f, 0.5f));
            ImguiNative.igSetNextWindowSize(new Vector2(windowWidth, windowHeight), ImGuiCond.Once);
            ImguiNative.igBegin("Cesium", null, (ImGuiWindowFlags)0);
            fixed (byte* accessTokenBufferPtr = accessTokenBuffer)
            {
                ImguiNative.igText("This is a demo of Evergine Cesium addon.");
                ImguiNative.igText("Please introduce a Cesium access token:");
                var textBoxSize = new Vector2(-float.Epsilon, ImguiNative.igGetTextLineHeight() * 10);
                ImguiNative.igInputTextMultiline("##access_token", accessTokenBufferPtr, (uint)accessTokenBuffer.Length, textBoxSize, ImGuiInputTextFlags.WordWrap, null, null);
                if (ImguiNative.igButton("connect", new Vector2(0, 0)))
                {
                    string accessToken = this.getAccessTokenStr();
                    this.CreateCesiumCoordinator(accessToken);
                    this.SaveAccessToken(saveAccessTokenFileName_cesium);
                    this.LoadSavedAccessToken(saveAccessTokenFileName_azure);
                    this.depthPicking = new DepthPicking(this.Managers.RenderManager);
                    this.graphicsPresenter.FocusedDisplay.MouseDispatcher.MouseButtonDown += this.OnMouseButtonDown;
                }

                var RED = new Vector4(1, 0, 0, 1);
                if (this.cesiumCoordinator != null)
                {
                    if (this.cesiumCoordinator.CurrentStatus == Status.AwaitingConnection)
                    {
                        ImguiNative.igText("Connecting to Cesium servers...");
                    }
                    else if (this.cesiumCoordinator.CurrentStatus == Status.CantAuthenticate)
                    {
                        ImguiNative.igTextColored(RED, "Authentication failed. Please check your access token and try again.");
                    }
                    else if (this.cesiumCoordinator.CurrentStatus == Status.CantReachEndpoint)
                    {
                        ImguiNative.igTextColored(RED, "Unable to connect to endpoint");
                    }
                    else if (this.cesiumCoordinator.CurrentStatus == Status.MissingTokenPermissions)
                    {
                        ImguiNative.igTextColored(RED, "Missing token permissions. Please check your access token and try again.");
                    }
                    else if (this.cesiumCoordinator.CurrentStatus == Status.UnknownError)
                    {
                        ImguiNative.igTextColored(RED, "Unknown Error");
                    }
                }
            }
            ImguiNative.igEnd();
        }

        private unsafe void gui_mainMenu()
        {
            ImguiNative.igSetNextWindowPos(new Vector2(0, 0) * 0.5f, ImGuiCond.Once, new Vector2(0.5f, 0.5f));
            ImguiNative.igSetNextWindowSize(new Vector2(120, 400), ImGuiCond.Once);
            ImguiNative.igBegin("Cesium", null, (ImGuiWindowFlags)0);
            { // Provider selection
                var currentProvider = cesiumCoordinator.OverlayProvider;
                var availableProviders = new[] {
                    TerrainOverlayProvider.BingAerial,
                    TerrainOverlayProvider.BingAerialWithLabels,
                    TerrainOverlayProvider.BingRoads,
                    TerrainOverlayProvider.GoogleMapsSatellite,
                    TerrainOverlayProvider.GoogleMapsSatelliteWithLabels,
                    TerrainOverlayProvider.GoogleMapsRoads,
                    TerrainOverlayProvider.GoogleMapsLabelsOnly,
                    TerrainOverlayProvider.GoogleMapsContours
                };
                if (ImguiNative.igBeginCombo("Provider", currentProvider.ToString(), ImGuiComboFlags.None))
                {
                    for (int i = 0; i < availableProviders.Length; i++)
                    {
                        bool isSelected = currentProvider == availableProviders[i];
                        if (ImguiNative.igSelectable_Bool(availableProviders[i].ToString(), isSelected, ImGuiSelectableFlags.None, Vector2.Zero))
                        {
                            cesiumCoordinator.OverlayProvider = availableProviders[i];
                        }
                        if (isSelected)
                        {
                            ImguiNative.igSetItemDefaultFocus();
                        }
                    }
                    ImguiNative.igEndCombo();
                }
            }

            { // geocoding
                ImguiNative.igSeparatorText("Geocoding");
                if (this.cesiumCoordinator.IsGeocodingConfigured)
                {
                    fixed (byte* inputTextBufferPtr = this.inputTextBuffer)
                    {
                        if (ImguiNative.igInputTextWithHint("##geocoding_input", "Type city or address",
                            inputTextBufferPtr, (uint)this.inputTextBuffer.Length,
                            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackEdit, (ImGuiInputTextCallbackData * data) =>
                            {
                                if ((data->EventFlag & ImGuiInputTextFlags.CallbackEdit) != 0)
                                {
                                    // trigger autocomplete on every text change
                                    if (this.getInputTextStr().Length < 3)
                                    {
                                        this.geocodingSuggestions.Clear();
                                    }
                                    else
                                    {
                                        this.cesiumCoordinator.AutocompleteAsync(u8PtrToString(data->Buf))
                                            .ContinueWith((res) =>
                                            {
                                                this.geocodingSuggestions.Clear();
                                                this.geocodingSuggestions.AddRange(res.Result.Suggestions);
                                            });
                                    }
                                }
                                return 0;
                            }, null))
                        {
                            this.cesiumCoordinator.GeocodeAsync(this.getInputTextStr())
                                .ContinueWith((res) => {
                                    var firstResult = res.Result.FirstResult;
                                    if (firstResult != null)
                                    {
                                        this.cesiumCoordinator.WorldCamera.FlyTo(firstResult.Latitude, firstResult.Longitude, 2.0);
                                    }
                                });
                        }
                    }
                    foreach (var suggestion in this.geocodingSuggestions)
                    {
                        if (ImguiNative.igButton(suggestion.DisplayText, new Vector2(200, 0)))
                        {
                            this.cesiumCoordinator.WorldCamera.FlyTo(suggestion.Latitude, suggestion.Longitude, 2);
                        }
                    }
                }
                else
                {
                    ImguiNative.igText("To enable geocoding features, please input an Azure Maps access token:");
                    var textBoxSize = new Vector2(-float.Epsilon, ImguiNative.igGetTextLineHeight() * 10);
                    fixed (byte* accessTokenBufferPtr = accessTokenBuffer)
                    {
                        ImguiNative.igInputTextMultiline("##access_token", accessTokenBufferPtr, (uint)accessTokenBuffer.Length, textBoxSize, ImGuiInputTextFlags.WordWrap, null, null);
                    }
                    if (ImguiNative.igButton("connect", new Vector2(0, 0)))
                    {
                        this.cesiumCoordinator.AzureMapsKey = this.getAccessTokenStr();
                        this.SaveAccessToken(saveAccessTokenFileName_azure);
                    }
                }
            }

            { // pins
                ImguiNative.igSeparatorText("Pins");
                ImguiNative.igText("Right-click to place pins");
                int deleteInd = -1;
                for (int i = 0; i < this.placedPins.Count; i++)
                {
                    var pin = this.placedPins[i];
                    ImguiNative.igText($"{pin.Name}");
                    ImguiNative.igSameLine(0, 0);
                    if (ImguiNative.igButton($"GO##{i}", new Vector2(0, 0)))
                    {
                        var placeComp = pin.FindComponent<CesiumPlacerComponent>();
                        this.cesiumCoordinator.WorldCamera.FlyTo(MathHelper.ToDegrees(placeComp.Latitude), MathHelper.ToDegrees(placeComp.Longitude), 2.0);
                    }
                    ImguiNative.igSameLine(0, 0);
                    ImguiNative.igPushStyleColor_Vec4(ImGuiCol.Button, new Vector4(1, 0, 0, 1));
                    ImguiNative.igPushStyleColor_Vec4(ImGuiCol.ButtonHovered, new Vector4(1, 0.3f, 0.3f, 1));
                    ImguiNative.igPushStyleColor_Vec4(ImGuiCol.ButtonActive, new Vector4(1, 0.5f, 0.5f, 1));
                    if (ImguiNative.igButton($"delete##{i}", new Vector2(0, 0)))
                    {
                        deleteInd = i;
                    }
                    ImguiNative.igPopStyleColor(3);
                }
                if (deleteInd >= 0 && deleteInd < this.placedPins.Count)
                {
                    var pin = this.placedPins[deleteInd];
                    pin.Parent.DetachChild(pin);
                    pin.Destroy();
                    this.placedPins.RemoveRange(deleteInd, 1);
                }
            }
            ImguiNative.igEnd();
        }

        private unsafe void OnMouseButtonDown(object sender, MouseButtonEventArgs args)
        {
            if (!imguiWantsCaptureMouse() && args.Button == MouseButtons.Right)
            {
                System.Console.WriteLine($"Mouse Position: {args.Position}");
                bool ok = this.depthPicking.Pick(args.Position, out Vector3 viewSpacePos, out Vector3 worldSpacePos);
                System.Console.WriteLine($"Pick Success: {ok}, View Space Position: {viewSpacePos}, World Space Position: {worldSpacePos}");

                // compute rootSpace position
                var worldCamera = this.cesiumCoordinator.WorldCamera;
                var ECEFRight = Vector3.Cross(worldCamera.ECEFForward, worldCamera.ECEFUp);
                Vec3 cartesianCoord = worldCamera.ECEFPosition;
                cartesianCoord.X += viewSpacePos.X * ECEFRight.X + viewSpacePos.Y * worldCamera.ECEFUp.X - viewSpacePos.Z * worldCamera.ECEFForward.X;
                cartesianCoord.Y += viewSpacePos.X * ECEFRight.Y + viewSpacePos.Y * worldCamera.ECEFUp.Y - viewSpacePos.Z * worldCamera.ECEFForward.Y;
                cartesianCoord.Z += viewSpacePos.X * ECEFRight.Z + viewSpacePos.Y * worldCamera.ECEFUp.Z - viewSpacePos.Z * worldCamera.ECEFForward.Z;

                // compute geagraphics 3D coodinates
                Ellipsoid ellipsoid = Ellipsoid.Wgs84();
                Cartographic cartographic;
                ellipsoid.CartesianToCartographic(cartesianCoord, &cartographic);
                double latitudeDegrees = cartographic.Latitude * 180 / Math.PI;
                double longitudeDegrees = cartographic.Longitude * 180 / Math.PI;

                // place a pin at the picked position
                var placerComp = new CesiumPlacerComponent()
                {
                    Latitude = cartographic.Latitude,
                    Longitude = cartographic.Longitude,
                    Height = cartographic.Height,
                    AboveTerrain = false,
                    Rotation = Quaternion.Identity,
                };
                var pinEntity = this.pinModel.InstantiateModelHierarchy(this.assetsService);
                pinEntity.Name = $"Pin {this.placedPins.Count}";
                pinEntity.AddComponent(placerComp);
                pinEntity.AddComponent(new ConstantSizeBillboard() { Scale = 0.01f });
                this.placedPins.Add(pinEntity);
                this.cesiumCoordinator.Root.AddChild(pinEntity);
            }
        }
    }
}
