using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using KeyboardKey = Raylib_cs.KeyboardKey;
using Rectangle = Raylib_cs.Rectangle;
using RenderTexture2D = Raylib_cs.RenderTexture2D;

namespace RaylibErosionStandalone;

class App
{
    private float _daySpeed = 0.015f;

    private const int ClipShadersCount = 1; // number of shaders that use a clipPlane

    private const int ScreenWidth = 1280; // initial size of window
    private const int ScreenHeight = 720;
    private const float FboSize = 2.5f; // Fame Buffer Object
    private int _windowWidthBeforeFullscreen = ScreenWidth;
    private int _windowHeightBeforeFullscreen = ScreenWidth;
    private bool _windowSizeChanged; // set to true when switching to fullscreen

    private readonly List<Size> _displayResolutions =
    [
        new(320, 180), // 0
        new(640, 36), // 1
        new(1280, 720), // 2
        new(1600, 900), // 3
        new(1920, 1080) // 4
    ];

    private int _currentDisplayResolutionIndex = 2;

    private bool _useApplicationBuffer; // whether to use app buffer or not
    private bool _lockTo60Fps;

    private float _daytime = 0.4f; // range (0, 1) but is sent to shader as a range(-1, 1) normalized upon a unit sphere

    private bool _dayRunning = true; // if day is animating

    private float _sunAngle;

    private Vector4 _ambientColorAndIntensity = new(0.22f, 0.17f, 0.41f, 0.2f);


    private int _totalDroplets; // total amount of droplets simulated
    private int _dropletsSinceLastTreeRegen; // used to regenerate trees after certain droplets have fallen

    private Shader _postProcessShader;
    private RenderTexture2D _applicationBuffer;
    private RenderTexture2D _reflectionBuffer;
    private RenderTexture2D _refractionBuffer;

    private const float Radius = 100.0f;
    
    private Camera3D _camera;

    private readonly List<Light> _lights = [];

    private static readonly ClipShaders ClipShaders = new();

    private List<Vector4> _ambientColors = [];


    enum ModelId
    {
        SkyBox = 0,
        Clouds,
        Terrain,
        OceanFloor,
        Ocean,
        Trees,
    }

    class ModelInfo
    {
        public bool Visible = true;
        public readonly string Name;
        public readonly bool DisableCulling;
        public Model Model;

        public ModelInfo(string name, bool disableCulling = false)
        {
            this.Name = name;
            DisableCulling = disableCulling;
        }
    }

    private readonly Dictionary<ModelId, ModelInfo> _modelsInfo = new()
    {
        { ModelId.SkyBox, new("skybox", true) },
        { ModelId.Clouds, new("clouds", true) },
        { ModelId.Terrain, new("terrain") },
        { ModelId.OceanFloor, new("ocean floor") },
        { ModelId.Ocean, new("ocean") },
        { ModelId.Trees, new("trees") },
    };

    private bool _debugInfoIsVisible;
    private bool _animated = true;
    private float _nDaytime;

    private Clouds _clouds = new();
    private SkyBox _skyBox = new();
    private Terrain _terrain = new();
    private Trees _trees = new();
    private Ocean _ocean = new();
    private OceanFloor _oceanFloor = new();

    public void Run()
    {
        unsafe
        {
            var ambientColorsImage = Raylib.LoadImage("resources/ambientGradient.png");

            var colors = Raylib.LoadImageColors(ambientColorsImage);
            if (colors == null)
                throw new FileLoadException("ambientGradient can't be read");

            _ambientColors = [];
            for (var i = 0; i < ambientColorsImage.Width; i++)
                _ambientColors.Add(Raylib.ColorNormalize(colors[i]));
            
            Raylib.UnloadImage(ambientColorsImage);

            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint |
                                  ConfigFlags.ResizableWindow); // Enable Multi Sampling Anti Aliasing 4x (if available)

            Raylib.InitWindow(ScreenWidth, ScreenHeight, "Terrain Erosion (.NET)");
            rlImGui.Setup();

            _postProcessShader = Raylib.LoadShader(null, "resources/shaders/postprocess.frag");
            // Create a RenderTexture2D to be used for render to texture
            _applicationBuffer =
                Raylib.LoadRenderTexture(Raylib.GetScreenWidth(),
                    Raylib.GetScreenHeight()); // main FBO used for postprocessing
            _reflectionBuffer = Raylib.LoadRenderTexture((int)(Raylib.GetScreenWidth() / FboSize),
                (int)(Raylib.GetScreenHeight() / FboSize)); // FBO used for water reflection
            _refractionBuffer = Raylib.LoadRenderTexture((int)(Raylib.GetScreenWidth() / FboSize),
                (int)(Raylib.GetScreenHeight() / FboSize)); // FBO used for water refraction
            Raylib.SetTextureFilter(_reflectionBuffer.Texture, TextureFilter.Bilinear);
            Raylib.SetTextureFilter(_refractionBuffer.Texture, TextureFilter.Bilinear);
            //SetTextureWrap(reflectionBuffer.texture, WRAP_CLAMP);
            //SetTextureWrap(refractionBuffer.texture, WRAP_CLAMP);

            // Define our custom camera to look into our 3d world
            _camera = new Camera3D(new Vector3(12.0f, 15, 22.0f),
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f),
                45.0f,
                CameraProjection.Perspective);
            //Raylib.SetCameraMode(camera, CAMERA_THIRD_PERSON);

            _modelsInfo[ModelId.Clouds].Model = _clouds.PrepareClouds();
            _modelsInfo[ModelId.Terrain].Model = _terrain.PrepareTerrain(ClipShaders, _ambientColorAndIntensity);
            _modelsInfo[ModelId.Ocean].Model = _ocean.PrepareOcean(_reflectionBuffer.Texture, _refractionBuffer.Texture);
            _modelsInfo[ModelId.OceanFloor].Model = _oceanFloor.PrepareOceanFloor(_terrain);

            //_modelsInfo[ModelId.SkyBox].Model = _skyBox.PrepareSkyBoxDayNightWithCubeMapShader();
            //_modelsInfo[ModelId.SkyBox].Model = _skyBox.PrepareSkyBoxStatic();
            _modelsInfo[ModelId.SkyBox].Model = _skyBox.PrepareSkyBoxDayNight();


            _terrain.PrepareInitialHeightMap();


            _trees.PrepareTrees(_terrain, _ambientColorAndIntensity);
            PrepareLights();

            Raylib.SetTargetFPS(60); // Set our game to run at 60 frames-per-second
            Raylib.SetTraceLogLevel(TraceLogLevel.None); // disable logging from now on


            while (!Raylib.WindowShouldClose())
            {
                HandleWindowResize();

                // Update
                //----------------------------------------------------------------------------------
                if (!Raylib.IsKeyDown(KeyboardKey.LeftAlt))
                {
                    if (!Raylib.IsCursorHidden())
                    {
                        Raylib.DisableCursor();
                    }

                    Raylib.UpdateCamera(ref _camera, CameraMode.FirstPerson); // Update camera
                }
                else
                {
                    if (Raylib.IsCursorHidden())
                        Raylib.EnableCursor();
                }

                if (_animated)
                {
                    _ocean.AnimateOcean();
                    _trees.AnimateTrees();
                    _clouds.AnimateClouds();
                    _skyBox.AnimateSkyBox();
                    AnimateDayTime();
                    AnimateLight();
                }

                _ocean.ApplyOceanMoveFactor();
                _trees.ApplyTreeMoveFactor();
                _clouds.ApplyCloudMoveFactor();
                _skyBox.ApplySkyBoxMoveFactor();
                ApplyDayTimeFactors();
                ApplyLightFactor();


                Raylib.BeginDrawing();
                rlImGui.Begin();

                _ocean.RenderOceanValues();
                RenderLightsValues();
                RenderCameraValues();

                Raylib.ClearBackground(Color.Black);


                {
                    // render stuff to reflection FBO
                    Raylib.BeginTextureMode(_reflectionBuffer);
                    Raylib.ClearBackground(Color.Red);
                    _camera.Position.Y *= -1;
                    Render3DScene(_camera, _lights, [ModelId.SkyBox, ModelId.Terrain], null, 1);
                    _camera.Position.Y *= -1;
                    Raylib.EndTextureMode();
                }

                {
                    // render stuff to refraction FBO
                    Raylib.BeginTextureMode(_refractionBuffer);
                    Raylib.ClearBackground(Color.Green);
                    Render3DScene(_camera, _lights, [ModelId.SkyBox, ModelId.Terrain, ModelId.OceanFloor], null, 0);
                    Raylib.EndTextureMode();
                }

                {
                    // render stuff to normal application buffer
                    if (_useApplicationBuffer) Raylib.BeginTextureMode(_applicationBuffer);
                    Raylib.ClearBackground(Color.DarkGreen);
                    Render3DScene(_camera, _lights,
                        [ModelId.SkyBox, ModelId.Clouds, ModelId.Terrain, ModelId.OceanFloor, ModelId.Ocean],
                        _trees._trees, 2);
                    if (_useApplicationBuffer) Raylib.EndTextureMode();
                }

                // render to frame buffer after applying post-processing (if enabled)
                if (_useApplicationBuffer)
                {
                    Raylib.BeginShaderMode(_postProcessShader);
                    // NOTE: Render texture must be y-flipped due to default OpenGL coordinates (left-bottom)
                    Raylib.DrawTextureRec(_applicationBuffer.Texture,
                        new Rectangle(0.0f, 0.0f, _applicationBuffer.Texture.Width, -_applicationBuffer.Texture.Height),
                        new Vector2(
                            0.0f, 0.0f),
                        Color.White);
                    Raylib.EndShaderMode();
                }

                Raylib.DrawFPS(10, 10);


                var hour = _daytime * 24.0f;
                var minute = (_daytime * 24.0f - hour) * 60.0f;
                // render GUI
                if (!Raylib.IsKeyDown(KeyboardKey.F6))
                {
                    if (!Raylib.IsKeyDown(KeyboardKey.F1))
                    {
                        Raylib.DrawText("Hold F1 to display controls. Hold ALT to enable cursor.", 10, 10, 20,
                            Color.White);
                        Raylib.DrawText($"Droplets simulated: {_totalDroplets}", 10, 40, 20, Color.White);
                        Raylib.DrawText($"FPS: {Raylib.GetFPS()}", 10, 70, 20, Color.White);
                        Raylib.DrawText($"{hour} : {minute}", Raylib.GetScreenWidth() - 80, 10, 20, Color.White);
                    }
                    else
                    {
                        var text = @"Z - hold to erode
                            X - press to erode 100000 droplets
                            R - press to reset island (chebyshev)
                            T - press to reset island (euclidean)
                            Y - press to reset island (manhattan)
                            U - press to reset island (star)
                            CTRL - toggle sun movement
                            Space - advance daytime
                            C - display frame buffers
                            V - display debug
                            O/L - camera Y
                            F2 - toggle 60 FPS lock
                            F3 - change window resolution
                            F4 - toggle fullscreen
                            F5 - toggle application buffer
                            F6 - hold to hide GUI
                            P - animation on/off
                            F9 - take screenshot";

                        Raylib.DrawText(
                            text,
                            10, 10, 20, Color.White);
                    }
                }


                {
                    var i = 0;
                    foreach (var (_, value) in _modelsInfo)
                    {
                        if (Raylib.IsKeyPressed(KeyboardKey.One + i)) value.Visible = !value.Visible;
                        i++;
                    }
                }


                if (Raylib.IsKeyDown(KeyboardKey.Z))
                {
                    // Erode
                    const int spd = 350;

                    _terrain.Erode(spd);

                    _totalDroplets += spd;
                    _dropletsSinceLastTreeRegen += spd;

                    if (_dropletsSinceLastTreeRegen > spd * 10)
                    {
                        _trees.GenerateTrees();
                        _dropletsSinceLastTreeRegen = 0;
                    }
                }

                if (Raylib.IsKeyPressed(KeyboardKey.X))
                {
                    // Erode
                    var stopwatch = new Stopwatch();
                    stopwatch.Start();
                    _terrain.Erode(100000);
                    stopwatch.Stop();
                    var elapsedTime = stopwatch.ElapsedMilliseconds;

                    Raylib.SetTraceLogLevel(TraceLogLevel.Info);
                    Raylib.TraceLog(TraceLogLevel.Info, $"Eroded 100000 droplets. Time elapsed: {elapsedTime} ms");
                    Raylib.SetTraceLogLevel(TraceLogLevel.None);

                    _totalDroplets += 100000;
                    
                    _trees.GenerateTrees();
                    _dropletsSinceLastTreeRegen = 0;
                }


                //if (Raylib.IsKeyPressed(KeyboardKey.R) || Raylib.IsKeyPressed(KeyboardKey.T) ||
                //    Raylib.IsKeyPressed(KeyboardKey.Y) ||
                //    Raylib.IsKeyPressed(KeyboardKey.U))
                //{
                //    _totalDroplets = 0;
                //    pixels = Raylib.LoadImageColors(initialHeightmapImage);
                //    for (var i = 0; i < MapResolution * MapResolution; i++)
                //    {
                //        _mapData[i] = pixels[i].R / 255.0f;
                //    }

                //    // reinit map
                //    if (Raylib.IsKeyPressed(KeyboardKey.R))
                //    {
                //        _erosionMaker.Gradient(ref _mapData, MapResolution, 0.5f, ErosionMaker.GradientType.SQUARE);
                //    }
                //    else if (Raylib.IsKeyPressed(KeyboardKey.T))
                //    {
                //        _erosionMaker.Gradient(ref _mapData, MapResolution, 0.5f, ErosionMaker.GradientType.CIRCLE);
                //    }
                //    else if (Raylib.IsKeyPressed(KeyboardKey.Y))
                //    {
                //        _erosionMaker.Gradient(ref _mapData, MapResolution, 0.5f, ErosionMaker.GradientType.DIAMOND);
                //    }
                //    else if (Raylib.IsKeyPressed(KeyboardKey.U))
                //    {
                //        _erosionMaker.Gradient(ref _mapData, MapResolution, 0.5f, ErosionMaker.GradientType.STAR);
                //    }

                //    _erosionMaker.Remap(ref _mapData, MapResolution); // flatten beaches
                //                                                      // no need to reinitialize erosion

                //    UpdatePixels(pixels);

                //    GenerateTrees(_erosionMaker, ref _mapData, _treeTextures, ref _trees, false);
                //    _dropletsSinceLastTreeRegen = 0;
                //}

                if (Raylib.IsKeyDown(KeyboardKey.O))
                {
                    _camera.Position.Y += 0.1f;
                }

                if (Raylib.IsKeyDown(KeyboardKey.L))
                {
                    _camera.Position.Y -= 0.1f;
                }

                if (Raylib.IsKeyPressed(KeyboardKey.P))
                    _animated = !_animated;

                if (Raylib.IsKeyDown(KeyboardKey.C))
                {
                    // display FBOS for debug
                    Raylib.DrawTextureRec(_reflectionBuffer.Texture,
                        new Rectangle(0.0f, 0.0f, _reflectionBuffer.Texture.Width, -_reflectionBuffer.Texture.Height),
                        new Vector2(
                            0.0f, 0.0f),
                        Color.White);
                    Raylib.DrawTextureRec(_refractionBuffer.Texture,
                        new Rectangle(0.0f, 0.0f, _refractionBuffer.Texture.Width, -_refractionBuffer.Texture.Height),
                        new Vector2(
                            0.0f, _reflectionBuffer.Texture.Height), Color.White);
                }

                if (Raylib.IsKeyPressed(KeyboardKey.V))
                    _debugInfoIsVisible = !_debugInfoIsVisible;

                if (_debugInfoIsVisible)
                {
                    RenderDebugInfo();
                }

                if (Raylib.IsKeyPressed(KeyboardKey.LeftControl))
                {
                    _dayRunning = !_dayRunning;
                }

                if (Raylib.IsKeyPressed(KeyboardKey.F2))
                {
                    if (_lockTo60Fps)
                    {
                        _lockTo60Fps = false;
                        Raylib.SetTargetFPS(0);
                    }
                    else
                    {
                        _lockTo60Fps = true;
                        Raylib.SetTargetFPS(60);
                    }
                }

                if (Raylib.IsKeyPressed(KeyboardKey.F3))
                {
                    _currentDisplayResolutionIndex++;
                    if (_currentDisplayResolutionIndex > 4)
                        _currentDisplayResolutionIndex = 0;

                    _windowSizeChanged = true;
                    Raylib.SetWindowSize(_displayResolutions[_currentDisplayResolutionIndex].Width,
                        _displayResolutions[_currentDisplayResolutionIndex].Height);
                    Raylib.SetWindowPosition((Raylib.GetMonitorWidth(0) - Raylib.GetScreenWidth()) / 2,
                        (Raylib.GetMonitorHeight(0) - Raylib.GetScreenHeight()) / 2);
                }

                if (Raylib.IsKeyPressed(KeyboardKey.F4))
                {
                    _windowSizeChanged = true;
                    if (!Raylib.IsWindowFullscreen())
                    {
                        _windowWidthBeforeFullscreen = Raylib.GetScreenWidth();
                        _windowHeightBeforeFullscreen = Raylib.GetScreenHeight();
                        Raylib.SetWindowSize(Raylib.GetMonitorWidth(0), Raylib.GetMonitorHeight(0));
                    }
                    else
                    {
                        Raylib.SetWindowSize(_windowWidthBeforeFullscreen, _windowHeightBeforeFullscreen);
                    }

                    Raylib.ToggleFullscreen();
                }


                if (Raylib.IsKeyPressed(KeyboardKey.F5))
                {
                    _useApplicationBuffer = !_useApplicationBuffer;
                }

                if (Raylib.IsKeyPressed((KeyboardKey.F9)))
                {
                    // take a screenshot
                    var fileName = $"screenshot-{DateTime.Now}.png";
                    Raylib.TakeScreenshot(fileName);
                }


                rlImGui.End();
                Raylib.EndDrawing();
            }

            // technically not required
            Raylib.UnloadRenderTexture(_applicationBuffer);
            Raylib.UnloadRenderTexture(_reflectionBuffer);
            Raylib.UnloadRenderTexture(_refractionBuffer);

            // Close window and OpenGL context
            rlImGui.Shutdown();
        }
    }

    

    private void RenderDebugInfo()
    {
        //var destinationWidth = 400;
        //var destinationHeight = 300;
        //var texture = Raylib.GetMaterialTexture(ref _skyboxModel, 0, MaterialMapIndex.Cubemap);
        //Raylib.DrawTexturePro(texture,
        //    new Rectangle(0, 0, texture.Width, texture.Height),
        //    new Rectangle(Raylib.GetScreenWidth() - destinationWidth,
        //        0,
        //        destinationWidth, destinationHeight),
        //    Vector2.Zero, 
        //    0f,
        //    Color.White);

        //var texture = Raylib.GetMaterialTexture(ref _skyboxModel, 0, MaterialMapIndex.Cubemap);
        //Raylib.DrawTextureEx(texture,
        //    new Vector2(Raylib.GetScreenWidth() - texture.Width - 20.0f, 20),
        //    0f, 1f,
        //    Color.White);

        //Raylib.DrawTextureEx(_heightmapTexture,
        //    new Vector2(Raylib.GetScreenWidth() - _heightmapTexture.Width - 20.0f, 20),
        //    0f, 1f,
        //    Color.White);
        //Raylib.DrawRectangleLines(Raylib.GetScreenWidth() - _heightmapTexture.Width - 20, 20,
        //    _heightmapTexture.Width,
        //    _heightmapTexture.Height,
        //    Color.Green);

        var text = "";
        foreach (var (_, value) in _modelsInfo)
        {
            text += $"{value.Name}={value.Visible}\n";
        }

        text += $"camera={_camera.Position}\n";

        Raylib.DrawText(
            text,
            10, Raylib.GetScreenHeight() / 2, 20, Color.White);
    }


    private void ApplyLightFactor()
    {
        Rlights.UpdateLightValues(_lights[0]);


        unsafe
        {
            // Update the light shader with the camera view position
            var shader = Raylib.GetMaterial(ref _terrain.Model, 0).Shader;
            Raylib.SetShaderValue(shader,
                shader.Locs[(int)ShaderLocationIndex.VectorView],
                _camera.Position,
                ShaderUniformDataType.Vec3);


            int locIndex = _ocean.Model.Materials[0].Shader.Locs[(int)ShaderLocationIndex.VectorView];
            if(locIndex < 0)
                Raylib.TraceLog(TraceLogLevel.Error, "VectorView loc index < 0");
            shader = Raylib.GetMaterial(ref _ocean.Model, 0).Shader;
            Raylib.SetShaderValue(shader,
                locIndex,
                _camera.Position,
                ShaderUniformDataType.Vec3);
        }
    }

    private void ApplyDayTimeFactors()
    {
        unsafe
        {
            Raylib.SetShaderValue(_terrain.Model.Materials[0].Shader, _terrain.TerrainDaytimeLoc, _nDaytime,
                ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_skyBox._skyboxModel.Materials[0].Shader, _skyBox._skyboxDaytimeLoc, _nDaytime,
                ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_skyBox._skyboxModel.Materials[0].Shader, _skyBox._skyboxDayRotationLoc, _daytime,
                ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_clouds.Model.Materials[0].Shader, _clouds._cloudDaytimeLoc, _nDaytime,
                ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_terrain.Model.Materials[0].Shader, _terrain.TerrainAmbientLoc, _ambientColorAndIntensity,
                ShaderUniformDataType.Vec4);
            Raylib.SetShaderValue(_trees._treeShader, _trees._treeAmbientLoc, _ambientColorAndIntensity, ShaderUniformDataType.Vec4);
        }
    }





    private void AnimateLight()
    {
        // Make the light orbit
        _lights[0].Position.X = (float)(Math.Cos(_sunAngle) * Radius);
        _lights[0].Position.Y = (float)(Math.Sin(_sunAngle) * Radius);
        _lights[0].Position.Z =
            (float)Math.Max(Math.Sin(_sunAngle) * Radius * 0.9f, -Radius / 4.0f); // skew sun orbit

    }

    private void AnimateDayTime()
    {
        if (_dayRunning)
        {
            _daytime += _daySpeed * Raylib.GetFrameTime();
            while (_daytime > 1.0f)
                _daytime -= 1.0f;
        }

        if (Raylib.IsKeyDown(KeyboardKey.Space))
        {
            //TODO check if equivalence is OK. bool was used in the equation!
            _daytime += _daySpeed * (5.0f - (_dayRunning ? 1.0f : 0.0f)) * Raylib.GetFrameTime();
            //_daytime += _daySpeed * (5.0f - (float)_dayRunning) * Raylib.GetFrameTime();
            while (_daytime > 1.0f)
                _daytime -= 1.0f;
        }

        _sunAngle = Tools.Lerp(-90, 270, _daytime) * Raylib.DEG2RAD; // -90 midnight, 90 midday

        // normalize it to make it look like a dot product on an unit sphere (shaders expect it this way) (-1, 1)
        _nDaytime = (float)Math.Sin(_sunAngle);
        var iDaytime = (int)(((_nDaytime + 1.0f) / 2.0f) * (_ambientColors.Count - 1));
        _ambientColorAndIntensity[0] = _ambientColors[iDaytime].X; // ambient color based on daytime
        _ambientColorAndIntensity[1] = _ambientColors[iDaytime].Y;
        _ambientColorAndIntensity[2] = _ambientColors[iDaytime].Z;
        _ambientColorAndIntensity[3] = Tools.Lerp(0.05f, 0.25f, ((_nDaytime + 1.0f) / 2.0f)); // ambient strength based on daytime
    }



    private void HandleWindowResize()
    {
        if (Raylib.IsWindowResized() == false
            && _windowSizeChanged == false)
            return;

        _windowSizeChanged = false;

        unsafe
        {
            // resize fbos based on screen size
            Raylib.UnloadRenderTexture(_applicationBuffer);
            Raylib.UnloadRenderTexture(_reflectionBuffer);
            Raylib.UnloadRenderTexture(_refractionBuffer);
            _applicationBuffer =
                Raylib.LoadRenderTexture(Raylib.GetScreenWidth(),
                    Raylib.GetScreenHeight()); // main FBO used for postprocessing
            _reflectionBuffer =
                Raylib.LoadRenderTexture((int)(Raylib.GetScreenWidth() / FboSize),
                    (int)(Raylib.GetScreenHeight() / FboSize)); // FBO used for water reflection
            _refractionBuffer =
                Raylib.LoadRenderTexture((int)(Raylib.GetScreenWidth() / FboSize),
                    (int)(Raylib.GetScreenHeight() / FboSize)); // FBO used for water refraction
            Raylib.SetTextureFilter(_reflectionBuffer.Texture, TextureFilter.Bilinear);
            Raylib.SetTextureFilter(_refractionBuffer.Texture, TextureFilter.Bilinear);

            // to be sure
            _ocean.Model.Materials[0].Maps[0].Texture = _reflectionBuffer.Texture; // uniform texture0
            _ocean.Model.Materials[0].Maps[1].Texture = _refractionBuffer.Texture; // uniform texture1

            Raylib.SetTraceLogLevel(TraceLogLevel.Info);
            Raylib.TraceLog(TraceLogLevel.Info,
                $"Window resized: {Raylib.GetScreenWidth()} x {Raylib.GetScreenHeight()}");
            Raylib.SetTraceLogLevel(TraceLogLevel.None);
        }
    }

    private void PrepareLights()
    {
        unsafe
        {
            var light = Rlights.CreateLight(
                LightType.Directional,
                new Vector3(20, 10, 0),
                Vector3.Zero,
                Color.White,
                [
                    _terrain.Model.Materials[0].Shader, _ocean.Model.Materials[0].Shader, _trees._treeShader,
                    _skyBox._skyboxModel.Materials[0].Shader
                ]
            );
            _lights.Add(light);
        }
    }


    /// <summary>
    /// renders all 3d scene (include variants for above and below the surface)
    /// </summary>
    /// <param name="camera"></param>
    /// <param name="lights"></param>
    /// <param name="modelIds"></param>
    /// <param name="trees"></param>
    /// <param name="clipPlane">0 = cull above, 1 = cull below, 2 = no cull</param>
    void Render3DScene(Camera3D camera, List<Light> lights, List<ModelId> modelIds, List<TreeBillboard>? trees,
        int clipPlane)
    {
        Raylib.BeginMode3D(camera);
        for (var i = 0; i < ClipShadersCount; i++) // setup clip plane for shaders that use it
        {
            Raylib.SetShaderValue(ClipShaders.clipShaders[i], ClipShaders.clipShaderTypeLocs[i], clipPlane,
                ShaderUniformDataType.Int);
        }

        foreach (var modelId in modelIds)
        {
            var modelInfo = _modelsInfo[modelId];
            if (modelInfo.Visible == false)
                continue;

            if (modelInfo.DisableCulling)
            {
                Rlgl.DisableBackfaceCulling();
                Rlgl.DisableDepthMask();
            }

            Raylib.DrawModel(modelInfo.Model, Vector3.Zero, 1.0f, Color.White);
            if (modelInfo.DisableCulling)
            {
                Rlgl.EnableBackfaceCulling();
                Rlgl.EnableDepthMask();
            }
        }

        if (_modelsInfo[ModelId.Trees].Visible
            && trees != null)
        {
            Raylib.BeginShaderMode(_trees._treeShader);
            for (var i = 0; i < trees.Count(); i++) // draw all trees
            {
                Raylib.DrawBillboard(camera, trees[i].texture, trees[i].position, trees[i].scale, trees[i].color);
            }

            Raylib.EndShaderMode();
        }

        if (_debugInfoIsVisible)
        {
            // Draw markers to show where the lights are
            foreach (var light in lights)
            {
                if (light.Enabled)
                {
                    Raylib.DrawSphereEx(light.Position,
                        20, 8, 8, Color.Red);
                }
                //if (light.Enabled)
                //{
                //    Raylib.DrawSphereEx(Raymath.Vector3Scale(light.Position, 10),
                //        100, 8, 8, Color.Red);
                //}
            }
        }

        Raylib.EndMode3D();
    }


    private void RenderLightsValues()
    {
        if (ImGui.Begin("Lights"))
        {
            var light = _lights[0];

            ImGui.Checkbox("Enabled", ref light.Enabled);
            var type = (int)light.Type;
            ImGui.InputInt("Type", ref type);
            ImGui.InputFloat3("Position", ref light.Position);
            ImGui.InputFloat3("Target", ref light.Target);

            var color = new Vector4(light.Color.R / (float)255, light.Color.G / (float)255,
                light.Color.B / (float)255, light.Color.A / (float)255);

            ImGui.ColorEdit4("Color", ref color);

            ImGui.End();
        }
    }

    private void RenderCameraValues()
    {
        if (ImGui.Begin("Cameras"))
        {
            var camera = _camera;

            ImGui.InputFloat3("Position", ref camera.Position);
            ImGui.InputFloat3("Target", ref camera.Target);
            ImGui.InputFloat("FovY", ref camera.FovY);

            ImGui.End();
        }
    }
}