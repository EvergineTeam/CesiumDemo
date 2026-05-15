using Evergine.Bindings.CesiumNative;
using Evergine.Bindings.Imgui;
using Evergine.Cesium;
using Evergine.Cesium.Components;
using Evergine.Cesium.Utils;
using Evergine.Common.Graphics;
using Evergine.Common.Input.Mouse;
using Evergine.Components.Graphics3D;
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
using Newtonsoft.Json.Linq;
using SharpYaml.Tokens;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using static Azure.Core.HttpHeader;
using static CesiumDemo.EvergineContent;
using static Evergine.Bindings.Draco.Draco;

namespace CesiumDemo
{
    public class MyScene : Scene
    {
        private static readonly string saveAccessTokenFileName_cesium = "cesium_access_token.txt";
        private static readonly string saveAccessTokenFileName_azure = "azure_access_token.txt";
        private byte[] accessTokenBuffer = new byte[512];
        private byte[] geocodingTextBuffer = new byte[256];
        private byte[] pinNameTextBuffer = new byte[256];

        private GraphicsPresenter graphicsPresenter;
        private AssetsService assetsService;
        private RenderLayerDescription opaqueLayer;
        private CesiumCoordinator cesiumCoordinator = null;
        private DepthPicking depthPicking;
        private Model pinModel = null;
        private Effect pinsEfect = null;
        private List<Entity> placedPins = new List<Entity>();
        private List<StandardMaterial> placedPinsMaterials = new List<StandardMaterial>();
        private List<GeocodingAutocompleteSuggestion> geocodingSuggestions = new List<GeocodingAutocompleteSuggestion>();
        private int edittingPinName = -1;
        private bool needSetKeyboardFocus = false; // true if we just started editting the pin name, so we still need to set the focus on the InputText box

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
            this.pinModel = await GLBRuntime.Instance.Read("Meshes/pin.glb");
            this.pinsEfect = this.assetsService.Load<Effect>(DefaultResourcesIDs.StandardEffectID);
            this.opaqueLayer = this.assetsService.Load<RenderLayerDescription>(DefaultResourcesIDs.OpaqueRenderLayerID);
        }

        private static unsafe string bytePtrToString(byte* bufferPtr)
        {
            return System.Runtime.InteropServices.Marshal.PtrToStringUTF8((IntPtr)bufferPtr);
        }

        private static unsafe string byteArrayToString(byte[] buffer)
        {
            fixed (byte* bufferPtr = buffer)
            {
                return bytePtrToString(bufferPtr);
            }
        }

        private string getAccessTokenStr() => byteArrayToString(accessTokenBuffer);
        private string getInputTextStr() => byteArrayToString(geocodingTextBuffer);

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

            var worldCamera = this.cesiumCoordinator?.WorldCamera;
            if (worldCamera != null)
            {
                worldCamera.UIHasFocus = imguiWantsCaptureMouse();
            }
        }

        protected override void Draw(TimeSpan deltaTime)
        {
            base.Draw(deltaTime);

            //var lines = (this.Managers.RenderManager as RenderManager).LineBatch3D;
            //foreach (var pinEntity in this.placedPins)
            //{
            //    var transform = pinEntity.FindComponent<Transform3D>();
            //    var box = new BoundingOrientedBox(transform.Position, transform.Scale, transform.Orientation);
            //    lines.DrawBoundingOrientedBox(box, Color.Red);
            //}

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
            ImguiNative.igBegin("Cesium##start", null, (ImGuiWindowFlags)0);
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
            ImguiNative.igSetNextWindowPos(new Vector2(0, 0), ImGuiCond.FirstUseEver, new Vector2(0, 0));
            ImguiNative.igSetNextWindowSize(new Vector2(290, 500), ImGuiCond.FirstUseEver);
            ImguiNative.igBegin("Cesium##main", null, (ImGuiWindowFlags)0);
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
                    fixed (byte* inputTextBufferPtr = this.geocodingTextBuffer)
                    {
                        if (ImguiNative.igInputTextWithHint("##geocoding_input", "Type city or address",
                            inputTextBufferPtr, (uint)this.geocodingTextBuffer.Length,
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
                                        this.cesiumCoordinator.AutocompleteAsync(bytePtrToString(data->Buf))
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
                    ImguiNative.igText("To enable geocoding features,\nplease input an Azure Maps access token:");
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
                    ImguiNative.igPushID_Int(0);
                    var pin = this.placedPins[i];
                    var pinMaterial = this.placedPinsMaterials[i];
                    if (ImguiNative.igColorButton($"##color_{i}", pinMaterial.BaseColor.ToVector4(), ImGuiColorEditFlags.NoTooltip, new Vector2(0, 0)))
                    {
                        ImguiNative.igOpenPopup_Str($"pin_color_popup_{i}", ImGuiPopupFlags.None);
                    }
                    if (ImguiNative.igBeginPopup($"pin_color_popup_{i}", ImGuiWindowFlags.None))
                    {
                        var paletteColors = new Vector4[]
                        {
                           new Vector4(1, 0, 0, 1),
                           new Vector4(0, 1, 0, 1),
                           new Vector4(0, 0, 1, 1),
                           new Vector4(1, 1, 0, 1),
                           new Vector4(0, 1, 1, 1),
                           new Vector4(1, 0, 1, 1),
                           new Vector4(0, 0, 0, 1),
                           new Vector4(1, 1, 1, 1),
                        };
                        for (int pi = 0; pi < paletteColors.Length; pi++)
                        {
                            if (ImguiNative.igColorButton($"##palette_{pi}", paletteColors[pi], ImGuiColorEditFlags.NoTooltip, new Vector2(0, 0)))
                            {
                                pinMaterial.BaseColor = Color.FromVector4(ref paletteColors[pi]);
                                ImguiNative.igCloseCurrentPopup();
                            }
                            if (pi % 4 != 3)
                            {
                                ImguiNative.igSameLine(0, 1);
                            }
                        }
                        ImguiNative.igEndPopup();
                        ImguiNative.igPopID();

                    }
                    ImguiNative.igSameLine(0, 1);

                    if (this.edittingPinName == i)
                    {
                        fixed (byte* pinNameTextBufferPtr = this.pinNameTextBuffer)
                        {
                            if (this.needSetKeyboardFocus)
                            {
                                ImguiNative.igSetKeyboardFocusHere(0);
                            }
                            if (ImguiNative.igInputText($"##edit_name_{i}", pinNameTextBufferPtr, (uint)pinNameTextBuffer.Length, ImGuiInputTextFlags.EnterReturnsTrue, null, null))
                            {
                                pin.Name = bytePtrToString(pinNameTextBufferPtr);
                                this.edittingPinName = -1;
                            }
                        }
                    }
                    else
                    {
                        ImguiNative.igText($"{pin.Name}");
                        if (ImguiNative.igBeginPopupContextItem($"pin_right_click{i}", ImGuiPopupFlags.None))
                        {
                            if (ImguiNative.igSelectable_Bool("Edit Name##{i}", false, ImGuiSelectableFlags.None, new Vector2(0, 0)))
                            {
                                this.edittingPinName = i;
                                this.needSetKeyboardFocus = true;
                                for (int nameI = 0; nameI < pin.Name.Length && nameI < this.pinNameTextBuffer.Length; nameI++)
                                {
                                    this.pinNameTextBuffer[nameI] = (byte)pin.Name[nameI];
                                }
                                this.pinNameTextBuffer[pin.Name.Length] = 0;
                            }
                            ImguiNative.igPushStyleColor_Vec4(ImGuiCol.Text, new Vector4(1, 0, 0, 1));
                            if (ImguiNative.igSelectable_Bool("Delete Pin##{i}", false, ImGuiSelectableFlags.None, new Vector2(0, 0)))
                            {
                                deleteInd = i;
                            }
                            ImguiNative.igPopStyleColor(1);
                            ImguiNative.igEndPopup();
                        }
                    }

                    ImguiNative.igSameLine(0, 1);
                    if (ImguiNative.igButton($"GO##{i}", new Vector2(0, 0)))
                    {
                        var placeComp = pin.FindComponent<CesiumPlacerComponent>();
                        this.cesiumCoordinator.WorldCamera.FlyTo(MathHelper.ToDegrees(placeComp.Latitude), MathHelper.ToDegrees(placeComp.Longitude), 2.0);
                    }
                    //ImguiNative.igSameLine(0, 1);
                    //ImguiNative.igPushStyleColor_Vec4(ImGuiCol.Button, new Vector4(1, 0, 0, 1));
                    //ImguiNative.igPushStyleColor_Vec4(ImGuiCol.ButtonHovered, new Vector4(1, 0.3f, 0.3f, 1));
                    //ImguiNative.igPushStyleColor_Vec4(ImGuiCol.ButtonActive, new Vector4(1, 0.5f, 0.5f, 1));
                    //if (ImguiNative.igButton($"delete##{i}", new Vector2(0, 0)))
                    //{
                    //    deleteInd = i;
                    //}
                    //ImguiNative.igPopStyleColor(3);
                }
                if (deleteInd >= 0 && deleteInd < this.placedPins.Count)
                {
                    var pin = this.placedPins[deleteInd];
                    pin.Parent.DetachChild(pin);
                    pin.Destroy();
                    this.placedPins.RemoveRange(deleteInd, 1);
                    this.placedPinsMaterials.RemoveRange(deleteInd, 1);
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
                    Rotation = Quaternion.CreateFromYawPitchRoll(0.4f, -0.9f, 0),
                };
                var pinEntity = this.pinModel.InstantiateModelHierarchy(this.assetsService);
                pinEntity.Name = $"Pin {this.placedPins.Count}";
                pinEntity.AddComponent(placerComp);
                pinEntity.AddComponent(new ConstantSizeBillboard() { Scale = 0.01f });
                var materialComp = pinEntity.FindComponentInChildren<MaterialComponent>();
                var material = new StandardMaterial(this.pinsEfect)
                {
                    BaseColor = new Color(255, 0, 0, 255),
                    Metallic = 0,
                    Roughness = 1,
                    LayerDescription = opaqueLayer,
                };
                materialComp.Material = material.Material;
                this.placedPins.Add(pinEntity);
                this.placedPinsMaterials.Add(material);
                this.cesiumCoordinator.Root.AddChild(pinEntity);
            }
        }
    }
}
