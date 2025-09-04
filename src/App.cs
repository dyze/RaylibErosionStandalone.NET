using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;
using KeyboardKey = Raylib_cs.KeyboardKey;
using PixelFormat = Raylib_cs.PixelFormat;
using Rectangle = Raylib_cs.Rectangle;
using RenderTexture2D = Raylib_cs.RenderTexture2D;

namespace RaylibErosionStandalone;

class App
{
    private const int MapResolution = 512; // width and height of heightmap
    private const int ClipShadersCount = 1; // number of shaders that use a clipPlane
    private const int TreeTextureCount = 19; // number of textures for a tree
    private const int TreeCount = 8190; // number of tree billboards

    private Shader _treeShader; // shader used for tree billboards
    private int _treeAmbientLoc;

    private const int ScreenWidth = 1280; // initial size of window
    private const int ScreenHeight = 720;
    private const float FboSize = 2.5f; // Fame Buffer Object
    private int _windowWidthBeforeFullscreen = ScreenWidth;
    private int _windowHeightBeforeFullscreen = ScreenWidth;
    private bool _windowSizeChanged = false; // set to true when switching to fullscreen

    private List<Size> _displayResolutions =
    [
        new(320, 180), // 0
        new(640, 36), // 1
        new(1280, 720), // 2
        new(1600, 900), // 3
        new(1920, 1080) // 4
    ];

    private int _currentDisplayResolutionIndex = 2;

    private bool _useApplicationBuffer = false; // wether to use app buffer or not
    private bool _lockTo60Fps = false;

    private float _daytime = 0.2f; // range (0, 1) but is sent to shader as a range(-1, 1) normalized upon a unit sphere
    private float _dayspeed = 0.015f;
    private bool _dayrunning = true; // if day is animating

    private float _sunAngle;

    private Vector4 _ambc = new(0.22f, 0.17f, 0.41f, 0.2f); // current ambient color & intensity

    private List<TreeBillboard> _noTrees; // keep empty
    private List<TreeBillboard> _trees; // fill with tree data

    private int _totalDroplets = 0; // total amount of droplets simulated
    private int _dropletsSinceLastTreeRegen = 0; // used to regenerate trees after certain droplets have fallen

    private Shader _postProcessShader;
    private RenderTexture2D _applicationBuffer;
    private RenderTexture2D _reflectionBuffer;
    private RenderTexture2D _refractionBuffer;

    private float _angle = 6.282f;
    private float _radius = 100.0f;

    private Model _terrainModel;

    private Texture2D _dudvTex;

    private int _waterMoveFactorLoc;
    private float _waterMoveFactor = 0.0f;

    private Model _cloudModel;
    private Model _oceanModel;
    private Model _oceanFloorModel;

    private float _treeMoveFactor = 0.0f;
    private int _treeMoveFactorLoc;
    private List<Texture2D> _treeTextures;

    //private Shader _waterShader;
    private float _cloudMoveFactor = 0.0f;
    private int _cloudMoveFactorLoc;
    private int _cloudDaytimeLoc;

    private float _skyboxMoveFactor = 0.0f;
    private int _skyboxMoveFactorLoc;
    private int _skyboxDaytimeLoc;
    private int _skyboxDayrotationLoc;

    private Model _skybox;

    private Camera3D _camera = new();

    private List<Light> _lights = [];

    private ErosionMaker _erosionMaker;

    private static ClipShaders _clipShaders;

    private List<float> _mapData;
    private Texture2D _heightmapTexture;

    private List<Vector4> _ambientColors;
    private int _ambientColorsNumber;

    private Texture2D _terrainGradient;
    private int _terrainDaytimeLoc;
    private int _terrainAmbientLoc;

    public void Run()
    {
        unsafe
        {
            Image ambientColorsImage = Raylib.LoadImage("resources/ambientGradient.png");

            //TODO check if equivalence is correct
            var colors = Raylib.LoadImageColors(ambientColorsImage);
            _ambientColors = [];
            for (var i = 0; i < ambientColorsImage.Width; i++)
            {
                _ambientColors.Add(Raylib.ColorNormalize(colors[i]));
            }
            //_ambientColors = GetImageDataNormalized(ambientColorsImage); // array of colors for ambient color through the day

            _ambientColorsNumber = ambientColorsImage.Width; // length of array
            Raylib.UnloadImage(ambientColorsImage);

            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint |
                                  ConfigFlags.ResizableWindow); // Enable Multi Sampling Anti Aliasing 4x (if available)

            Raylib.InitWindow(ScreenWidth, ScreenHeight, "Terrain Erosion (.NET)");
            rlImGui.Setup();

            _postProcessShader = Raylib.LoadShader("", "resources/shaders/postprocess.frag");
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
            _camera = new Camera3D(new Vector3(12.0f, 32.0f, 22.0f),
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f),
                45.0f,
                CameraProjection.Perspective);
            //Raylib.SetCameraMode(camera, CAMERA_THIRD_PERSON);


            // Initialize the erosion maker
            _erosionMaker = ErosionMaker.GetInstance();


            Image initialHeightmapImage =
                Raylib.GenImagePerlinNoise(MapResolution, MapResolution, 50, 50, 4.0f); // generate fractal perlin noise
            _mapData = new List<float>(MapResolution * MapResolution);

            // Extract pixels and put them in mapData
            Color* pixels = Raylib.LoadImageColors(initialHeightmapImage);
            for (var i = 0; i < MapResolution * MapResolution; i++)
            {
                _mapData[i] = pixels[i].R / 255.0f;
            }

            Erode(pixels);


            PrepareTerrain();
            PrepareOcean();
            PrepareOceanFloor();
            PrepareClouds();
            PrepareSkyBox();
            PrepareTrees();
            PrepareLights();

            //rlDisableBackfaceCulling();

            Raylib.SetTargetFPS(0); // Set our game to run at 60 frames-per-second
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
                    Raylib.EnableCursor();
                }


                AnimateWater();
                AnimateTrees();
                AnimateClouds();
                AnimateDayTimeClouds();
                AnimateDayTime();
                AnimateLight();


                Raylib.BeginDrawing();


                // render stuff to reflection FBO
                Raylib.BeginTextureMode(_reflectionBuffer);
                Raylib.ClearBackground(Color.Red);
                _camera.Position.Y *= -1;
                Render3DScene(_camera, _lights, [_skybox, _terrainModel], _noTrees, 1);
                _camera.Position.Y *= -1;
                Raylib.EndTextureMode();

                // render stuff to refraction FBO
                Raylib.BeginTextureMode(_refractionBuffer);
                Raylib.ClearBackground(Color.Green);
                Render3DScene(_camera, _lights, [_skybox, _terrainModel, _oceanFloorModel], _noTrees, 0);
                Raylib.EndTextureMode();

                // render stuff to normal application buffer
                Raylib.BeginTextureMode(_applicationBuffer);
                Raylib.ClearBackground(Color.Yellow);
                Render3DScene(_camera, _lights, [_skybox, _cloudModel, _terrainModel, _oceanFloorModel, _oceanModel],
                    _noTrees, 2);
                if (_useApplicationBuffer) Raylib.EndTextureMode();

                // render to frame buffer after applying post-processing (if enabled)
                if (_useApplicationBuffer)
                {
                    Raylib.BeginShaderMode(_postProcessShader);
                    // NOTE: Render texture must be y-flipped due to default OpenGL coordinates (left-bottom)
                    Raylib.DrawTextureRec(_applicationBuffer.Texture,
                        new Rectangle(
                            0.0f, 0.0f, _applicationBuffer.Texture.Width, -_applicationBuffer.Texture.Height
                        ),
                        new Vector2(
                            0.0f, 0.0f
                        ),
                        Color.White);
                    Raylib.EndShaderMode();
                }

                float hour = _daytime * 24.0f;
                float minute = (_daytime * 24.0f - hour) * 60.0f;
                // render GUI
                if (!Raylib.IsKeyDown(KeyboardKey.F6))
                {
                    if (!Raylib.IsKeyDown(KeyboardKey.F1))
                    {
                        Raylib.DrawText("Hold F1 to display controls. Hold ALT to enable cursor.", 10, 10, 20, Color.White);
                        Raylib.DrawText($"Droplets simulated: {_totalDroplets}", 10, 40, 20, Color.White);
                        Raylib.DrawText($"FPS: {Raylib.GetFPS()}", 10, 70, 20, Color.White);
                        Raylib.DrawText($"{hour} : {minute}", Raylib.GetScreenWidth() - 80, 10, 20, Color.White);
                    }
                    else
                    {
                        Raylib.DrawText(
                            "Z - hold to erode\nX - press to erode 100000 droplets\nR - press to reset island (chebyshev)\nT - press to reset island (euclidean)\nY - press to reset island (manhattan)\nU - press to reset island (star)\nCTRL - toggle sun movement\nSpace - advance daytime\nS - display frame buffers\nA - display debug\nF2 - toggle 60 FPS lock\nF3 - change window resolution\nF4 - toggle fullscreen\nF5 - toggle application buffer\nF6 - hold to hide GUI\nF9 - take screenshot",
                            10, 10, 20, Color.White);
                    }
                }

                if (Raylib.IsKeyDown(KeyboardKey.Z))
                {
                    // Erode
                    const int spd = 350;
                    _erosionMaker.Erode(ref _mapData, MapResolution, spd, false);
                    _totalDroplets += spd;
                    _dropletsSinceLastTreeRegen += spd;

                    UpdatePixels(pixels);

                    if (_dropletsSinceLastTreeRegen > spd * 10)
                    {
                        GenerateTrees(_erosionMaker, ref _mapData, _treeTextures, ref _trees, false);
                        _dropletsSinceLastTreeRegen = 0;
                    }
                }

                if (Raylib.IsKeyPressed(KeyboardKey.X))
                {
                    unsafe
                    {
                        // Erode
                        var stopwatch = new Stopwatch();
                        stopwatch.Start();
                        _erosionMaker.Erode(ref _mapData, MapResolution, 100000, false);
                        stopwatch.Stop();
                        var elapsedTime = stopwatch.ElapsedMilliseconds;

                        Raylib.SetTraceLogLevel(TraceLogLevel.Info);
                        Raylib.TraceLog(TraceLogLevel.Info, $"Eroded 100000 droplets. Time elapsed: {elapsedTime} ms");
                        Raylib.SetTraceLogLevel(TraceLogLevel.None);

                        _totalDroplets += 100000;

                        UpdatePixels(pixels);

                        GenerateTrees(_erosionMaker, ref _mapData, _treeTextures, ref _trees, false);
                        _dropletsSinceLastTreeRegen = 0;
                    }
                }


                if (Raylib.IsKeyPressed(KeyboardKey.R) || Raylib.IsKeyPressed(KeyboardKey.T) ||
                    Raylib.IsKeyPressed(KeyboardKey.Y) ||
                    Raylib.IsKeyPressed(KeyboardKey.U))
                {
                    unsafe
                    {
                        _totalDroplets = 0;
                        pixels = Raylib.LoadImageColors(initialHeightmapImage);
                        for (var i = 0; i < MapResolution * MapResolution; i++)
                        {
                            _mapData[i] = pixels[i].R / 255.0f;
                        }

                        // reinit map
                        if (Raylib.IsKeyPressed(KeyboardKey.R))
                        {
                            _erosionMaker.Gradient(ref _mapData, MapResolution, 0.5f, ErosionMaker.GradientType.SQUARE);
                        }
                        else if (Raylib.IsKeyPressed(KeyboardKey.T))
                        {
                            _erosionMaker.Gradient(ref _mapData, MapResolution, 0.5f, ErosionMaker.GradientType.CIRCLE);
                        }
                        else if (Raylib.IsKeyPressed(KeyboardKey.Y))
                        {
                            _erosionMaker.Gradient(ref _mapData, MapResolution, 0.5f, ErosionMaker.GradientType.DIAMOND);
                        }
                        else if (Raylib.IsKeyPressed(KeyboardKey.U))
                        {
                            _erosionMaker.Gradient(ref _mapData, MapResolution, 0.5f, ErosionMaker.GradientType.STAR);
                        }

                        _erosionMaker.Remap(ref _mapData, MapResolution); // flatten beaches
                                                                          // no need to reinitialize erosion

                        UpdatePixels(pixels);

                        GenerateTrees(_erosionMaker, ref _mapData, _treeTextures, ref _trees, false);
                        _dropletsSinceLastTreeRegen = 0;
                    }
                }

                if (Raylib.IsKeyDown(KeyboardKey.S))
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

                if (Raylib.IsKeyDown(KeyboardKey.A))
                {
                    // display other info for debug
                    Raylib.DrawTextureEx(_heightmapTexture,
                        new Vector2(Raylib.GetScreenWidth() - _heightmapTexture.Width - 20.0f, 20),
                        0f, 1f,
                        Color.White);
                    Raylib.DrawRectangleLines(Raylib.GetScreenWidth() - _heightmapTexture.Width - 20, 20,
                        _heightmapTexture.Width,
                        _heightmapTexture.Height,
                        Color.Green);

                    //DrawFPS(10, 70);
                }

                if (Raylib.IsKeyPressed(KeyboardKey.LeftControl))
                {
                    _dayrunning = !_dayrunning;
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

                Raylib.DrawFPS(10, 10);

                rlImGui.End();
                Raylib.EndDrawing();
            }

            // technically not required
            Raylib.UnloadRenderTexture(_applicationBuffer);
            Raylib.UnloadRenderTexture(_reflectionBuffer);
            Raylib.UnloadRenderTexture(_refractionBuffer);

            // Close window and OpenGL context
            rlImGui.Shutdown();
            Raylib.CloseWindow();
        }
    }

    private unsafe void Erode(Color* pixels)
    {
        // Erode
        _erosionMaker.Gradient(ref _mapData, MapResolution, 0.5f,
            ErosionMaker.GradientType
                .SQUARE); // apply a centered gradient to smooth out border pixel (create island at center)
        _erosionMaker.Remap(ref _mapData, MapResolution); // flatten beaches
        _erosionMaker.Erode(ref _mapData, MapResolution, 0, true); // Erode (0 droplets for initialization)

        // Update pixels from mapData to texture
        UpdatePixels(pixels);
    }

    private unsafe void UpdatePixels(Color* pixels)
    {
        // Update pixels
        for (var i = 0; i < MapResolution * MapResolution; i++)
        {
            byte val = (byte)(_mapData[i] * 255);
            pixels[i].R = val;
            pixels[i].G = val;
            pixels[i].B = val;
            pixels[i].A = 255;
        }

        Raylib.UnloadTexture(_heightmapTexture);

        //TODO check if replacement for former "Image heightmapImage = Raylib.LoadImageEx(pixels, MapResolution, MapResolution);"
        Image heightmapImage = new Image();
        Raylib.GenImageColor(MapResolution, MapResolution, Color.Red);

        var k = 0;
        var dataAsChars = (char*)heightmapImage.Data;

        for (var i = 0; i < heightmapImage.Width * heightmapImage.Height * 4; i += 4)
        {
            dataAsChars[i] = (char)pixels[k].R;
            dataAsChars[i + 1] = (char)pixels[k].G;
            dataAsChars[i + 2] = (char)pixels[k].B;
            dataAsChars[i + 3] = (char)pixels[k].A;
            k++;
        }

        //END_TODO

        _heightmapTexture = Raylib.LoadTextureFromImage(heightmapImage); // Convert image to texture (VRAM)
        Raylib.SetTextureFilter(_heightmapTexture, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(_heightmapTexture, TextureWrap.Clamp);
        _terrainModel.Materials[0].Maps[2].Texture = _heightmapTexture;
        Raylib.UnloadImage(heightmapImage); // Unload heightmap image from RAM, already uploaded to VRAM
    }

    private void AnimateWater()
    {
        unsafe
        {
            // animate water
            _waterMoveFactor += 0.03f * Raylib.GetFrameTime();
            while (_waterMoveFactor > 1.0f)
            {
                _waterMoveFactor -= 1.0f;
            }

            Raylib.SetShaderValue(_oceanModel.Materials[0].Shader, _waterMoveFactorLoc, _waterMoveFactor,
                ShaderUniformDataType.Float);
        }
    }

    private void AnimateLight()
    {
        unsafe
        {
            // Make the light orbit
            _lights[0].Position.X = (float)(Math.Cos(_sunAngle) * _radius);
            _lights[0].Position.Y = (float)(Math.Sin(_sunAngle) * _radius);
            _lights[0].Position.Z = (float)Math.Max(Math.Sin(_sunAngle) * _radius * 0.9f, -_radius / 4.0f); // skew sun orbit

            Rlights.UpdateLightValues(_lights[0]);

            // Update the light shader with the camera view position
            Raylib.SetShaderValue(_terrainModel.Materials[0].Shader,
                _terrainModel.Materials[0].Shader.Locs[(int)ShaderLocationIndex.VectorView],
                _camera.Position,
                ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(_oceanModel.Materials[0].Shader, _oceanModel.Materials[0].Shader.Locs[(int)ShaderLocationIndex.VectorView],
                _camera.Position,
                ShaderUniformDataType.Vec3);
        }
    }

    private void AnimateDayTime()
    {
        unsafe
        {
            if (_dayrunning)
            {
                _daytime += _dayspeed * Raylib.GetFrameTime();
                while (_daytime > 1.0f)
                {
                    _daytime -= 1.0f;
                }
            }

            if (Raylib.IsKeyDown(KeyboardKey.Space))
            {
                //TODO check if equivalence is OK. bool was used in the equation!
                _daytime += _dayspeed * (5.0f - (_dayrunning ? 1.0f : 0.0f)) * Raylib.GetFrameTime();
                //_daytime += _dayspeed * (5.0f - (float)_dayrunning) * Raylib.GetFrameTime();
                while (_daytime > 1.0f)
                {
                    _daytime -= 1.0f;
                }
            }

            _sunAngle = Tools.Lerp(-90, 270, _daytime) * Raylib.DEG2RAD; // -90 midnight, 90 midday
            var nDaytime = (float)Math.Sin(_sunAngle); // normalize it to make it look like a dot product on an unit sphere (shaders expect it this way) (-1, 1)
            var iDaytime = (int)(((nDaytime + 1.0f) / 2.0f) * (float)(_ambientColorsNumber - 1));
            _ambc[0] = _ambientColors[iDaytime].X; // ambient color based on daytime
            _ambc[1] = _ambientColors[iDaytime].Y;
            _ambc[2] = _ambientColors[iDaytime].Z;
            _ambc[3] = Tools.Lerp(0.05f, 0.25f, ((nDaytime + 1.0f) / 2.0f)); // ambient strength based on daytime
            Raylib.SetShaderValue(_terrainModel.Materials[0].Shader, _terrainDaytimeLoc, nDaytime, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_skybox.Materials[0].Shader, _skyboxDaytimeLoc, nDaytime, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_skybox.Materials[0].Shader, _skyboxDayrotationLoc, _daytime, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_cloudModel.Materials[0].Shader, _cloudDaytimeLoc, nDaytime, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_terrainModel.Materials[0].Shader, _terrainAmbientLoc, _ambc, ShaderUniformDataType.Vec4);
            Raylib.SetShaderValue(_treeShader, _treeAmbientLoc, _ambc, ShaderUniformDataType.Vec4);
        }
    }

    private void AnimateDayTimeClouds()
    {
        unsafe
        {
            _skyboxMoveFactor += 0.0085f * Raylib.GetFrameTime();
            while (_skyboxMoveFactor > 1.0f)
            {
                _skyboxMoveFactor -= 1.0f;
            }

            Raylib.SetShaderValue(_skybox.Materials[0].Shader, _skyboxMoveFactorLoc, _skyboxMoveFactor,
                ShaderUniformDataType.Float);
        }
    }

    private void AnimateClouds()
    {
        unsafe
        {
            _cloudMoveFactor += 0.0032f * Raylib.GetFrameTime();
            while (_cloudMoveFactor > 1.0f)
            {
                _cloudMoveFactor -= 1.0f;
            }

            Raylib.SetShaderValue(_cloudModel.Materials[0].Shader, _cloudMoveFactorLoc, _cloudMoveFactor,
                ShaderUniformDataType.Float);
        }
    }

    private void AnimateTrees()
    {
        _treeMoveFactor += 0.125f * Raylib.GetFrameTime();
        while (_treeMoveFactor > 1.0f)
        {
            _treeMoveFactor -= 1.0f;
        }

        Raylib.SetShaderValue(_treeShader, _treeMoveFactorLoc, _treeMoveFactor, ShaderUniformDataType.Float);
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
            _oceanModel.Materials[0].Maps[0].Texture = _reflectionBuffer.Texture; // uniform texture0
            _oceanModel.Materials[0].Maps[1].Texture = _refractionBuffer.Texture; // uniform texture1

            Raylib.SetTraceLogLevel(TraceLogLevel.Info);
            Raylib.TraceLog(TraceLogLevel.Info, $"Window resized: {Raylib.GetScreenWidth()} x {Raylib.GetScreenHeight()}");
            Raylib.SetTraceLogLevel(TraceLogLevel.None);
        }
    }

    private void PrepareLights()
    {
        unsafe
        {
            _lights.Add(Rlights.CreateLight(
                LightType.Directional,
                new Vector3(20, 10, 0),
                Vector3.Zero,
                Color.White,
                [
                    _terrainModel.Materials[0].Shader, _oceanModel.Materials[0].Shader, _treeShader,
                    _skybox.Materials[0].Shader
                ]
            ));
        }
    }

    private void PrepareTrees()
    {
        unsafe
        {
            for (var i = 0; i < TreeTextureCount; i++)
            {
                _treeTextures[i] = Raylib.LoadTexture($"resources/trees/b/{i}.png"); // variant b of trees looks much better
                Raylib.SetTextureFilter(_treeTextures[i], TextureFilter.Bilinear);
                //GenTextureMipmaps(&treeTextures[i]); // looks better without
            }

            GenerateTrees(_erosionMaker, ref _mapData, _treeTextures, ref _trees, true);
            Material treeMaterial = Raylib.LoadMaterialDefault();
            _treeShader = Raylib.LoadShader("resources/shaders/vegetation.vert", "resources/shaders/vegetation.frag");
            _treeShader.Locs[(int)ShaderLocationIndex.MatrixModel] = Raylib.GetShaderLocation(_treeShader, "matModel");
            _treeAmbientLoc = Raylib.GetShaderLocation(_treeShader, "ambient");
            Raylib.SetShaderValue(_treeShader, _treeAmbientLoc, _ambc, ShaderUniformDataType.Vec4);
            treeMaterial.Shader = _treeShader;
            treeMaterial.Maps[1].Texture = _dudvTex;
            _treeMoveFactor = 0.0f;
            _treeMoveFactorLoc = Raylib.GetShaderLocation(_treeShader, "moveFactor");
        }
    }

    private void PrepareOceanFloor()
    {
        unsafe
        {
            Image whiteImage = Raylib.GenImageColor(8, 8, Color.Black);
            Texture2D whiteTexture = Raylib.LoadTextureFromImage(whiteImage);
            Raylib.UnloadImage(whiteImage);
            Mesh oceanFloorMesh = Raylib.GenMeshPlane(5120, 5120, 10, 10);
            Model oceanFloorModel = Raylib.LoadModelFromMesh(oceanFloorMesh);
            oceanFloorModel.Transform = Raymath.MatrixTranslate(0, -1.2f, 0);
            oceanFloorModel.Materials[0].Maps[0].Texture = _terrainGradient;
            oceanFloorModel.Materials[0].Maps[2].Texture = whiteTexture;
            oceanFloorModel.Materials[0].Shader = _terrainModel.Materials[0].Shader;
        }
    }

    // renders all 3d scene (include variants for above and below the surface)
    void Render3DScene(Camera3D camera, List<Light> lights, List<Model> models, List<TreeBillboard> trees,
        int clipPlane)
    {
        Raylib.BeginMode3D(camera);
        for (var i = 0; i < ClipShadersCount; i++) // setup clip plane for shaders that use it
        {
            Raylib.SetShaderValue(_clipShaders.clipShaders[i], _clipShaders.clipShaderTypeLocs[i], clipPlane, ShaderUniformDataType.Int);
        }

        for (var i = 0; i < models.Count(); i++) // draw all 3d models in the scene
        {
            Raylib.DrawModel(models[i], Vector3.Zero, 1.0f, Color.White);
        }

        Raylib.BeginShaderMode(_treeShader);
        for (var i = 0; i < trees.Count(); i++) // draw all trees
        {
            Raylib.DrawBillboard(camera, trees[i].texture, trees[i].position, trees[i].scale, trees[i].color);
        }

        Raylib.EndShaderMode();

        // Draw markers to show where the lights are
        /*for (size_t i = 0; i < MAX_LIGHTS; i++)
        {
            if (lights[i].enabled) { DrawSphereEx(Vector3Scale(lights[0].position, 50), 100, 8, 8, RED); }
        }*/
        Raylib.EndMode3D();
    }

    // generates (or regenerates) all tree billboards
    void GenerateTrees(ErosionMaker erosionMaker, ref List<float> mapData, List<Texture2D> treeTextures,
        ref List<TreeBillboard> trees, bool generateNew)
    {
        Vector3 billPosition = Vector3.Zero;
        Vector3 billNormal = Vector3.Zero;
        float grassSlopeThreshold = 0.2f; // different than in the terrain shader
        float grassBlendAmount = 0.55f;
        float grassWeight;
        Color billColor = Color.White;

        for (var i = 0; i < TreeCount; i++) // 8190 max billboards, more than that and they are not cached anymore
        {
            int px, py;
            do
            {
                // try to generate a billboard
                billPosition.X = Tools.randomRange(-16, 16);
                billPosition.Z = Tools.randomRange(-16, 16);
                px = (int)(((billPosition.X + 16.0f) / 32.0f) * (MapResolution - 1));
                py = (int)(((billPosition.Z + 16.0f) / 32.0f) * (MapResolution - 1));
                billNormal = erosionMaker.GetNormal(ref mapData, MapResolution, px, py);
                billPosition.Y = mapData[py * MapResolution + px] * 8 - 1.1f;

                float slope = 1.0f - billNormal.Y;
                float grassBlendHeight = grassSlopeThreshold * (1.0f - grassBlendAmount);
                grassWeight = 1.0f -
                              Math.Min(
                                  Math.Max((slope - grassBlendHeight) / (grassSlopeThreshold - grassBlendHeight), 0.0f),
                                  1.0f);
            } while (billPosition.Y < 0.32f || billPosition.Y > 3.25f ||
                     grassWeight < 0.65f); // repeat until you find valid parameters (height and normal of chosen spot)

            billColor.R = (byte)((billNormal.X + 1f) * 127.5f); // terrain normal where tree is located, stored on color
            billColor.G = (byte)((billNormal.Y + 1f) * 127.5f); // convert from range (-1, 1) to (0, 255)
            billColor.B = (byte)((billNormal.Z + 1f) * 127.5f);

            if (!generateNew)
            {
                trees[i].position = billPosition;
                trees[i].color = billColor;
            }
            else
            {
                var textureChoice = (int)Tools.randomRange(0, TreeTextureCount);
                trees.Add(new TreeBillboard(
                    treeTextures[textureChoice], billPosition, Tools.randomRange(0.6f, 1.4f) * 0.3f, billColor));
            }
        }
    }


    private unsafe void AnimateCloud()
    {
        // animate cirrostratus
        _cloudMoveFactor += 0.0032f * Raylib.GetFrameTime();
        while (_cloudMoveFactor > 1.0f)
        {
            _cloudMoveFactor -= 1.0f;
        }

        Raylib.SetShaderValue(_cloudModel.Materials[0].Shader, _cloudMoveFactorLoc, _cloudMoveFactor,
            ShaderUniformDataType.Float);
    }


    private unsafe void PrepareOcean()
    {
        Mesh oceanMesh = Raylib.GenMeshPlane(5120, 5120, 10, 10);
        Model oceanModel = Raylib.LoadModelFromMesh(oceanMesh);
        _dudvTex = Raylib.LoadTexture("resources/waterDUDV.png");
        Raylib.SetTextureFilter(_dudvTex, TextureFilter.Bilinear);
        Raylib.GenTextureMipmaps(ref _dudvTex);
        oceanModel.Transform = Raymath.MatrixTranslate(0, 0, 0);
        oceanModel.Materials[0].Maps[0].Texture = _reflectionBuffer.Texture; // uniform texture0
        oceanModel.Materials[0].Maps[1].Texture = _refractionBuffer.Texture; // uniform texture1
        oceanModel.Materials[0].Maps[2].Texture = _dudvTex; // uniform texture2
        oceanModel.Materials[0].Shader =
            Raylib.LoadShader("resources/shaders/water.vert", "resources/shaders/water.frag");
        _waterMoveFactor = 0.0f;
        _waterMoveFactorLoc = Raylib.GetShaderLocation(oceanModel.Materials[0].Shader, "moveFactor");
        oceanModel.Materials[0].Shader.Locs[(int)ShaderLocationIndex.MatrixModel] =
            Raylib.GetShaderLocation(oceanModel.Materials[0].Shader, "matModel");
        oceanModel.Materials[0].Shader.Locs[(int)ShaderLocationIndex.VectorView] =
            Raylib.GetShaderLocation(oceanModel.Materials[0].Shader, "viewPos");
    }

    private unsafe void PrepareClouds()
    {
        Texture2D cloudTexture = Raylib.LoadTexture("resources/clouds.png");
        Raylib.SetTextureFilter(cloudTexture, TextureFilter.Bilinear);
        Raylib.GenTextureMipmaps(&cloudTexture);
        Mesh cloudMesh = Raylib.GenMeshPlane(51200, 51200, 10, 10);
        Model cloudModel = Raylib.LoadModelFromMesh(cloudMesh);
        cloudModel.Transform = Raymath.MatrixTranslate(0, 1000.0f, 0);
        cloudModel.Materials[0].Shader =
            Raylib.LoadShader("resources/shaders/cirrostratus.vert", "resources/shaders/cirrostratus.frag");
        _cloudMoveFactor = 0.0f;
        _cloudMoveFactorLoc = Raylib.GetShaderLocation(cloudModel.Materials[0].Shader, "moveFactor");
        _cloudDaytimeLoc = Raylib.GetShaderLocation(cloudModel.Materials[0].Shader, "daytime");
        cloudModel.Materials[0].Shader.Locs[(int)ShaderLocationIndex.MatrixModel] =
            Raylib.GetShaderLocation(cloudModel.Materials[0].Shader, "matModel");
        cloudModel.Materials[0].Shader.Locs[(int)ShaderLocationIndex.VectorView] =
            Raylib.GetShaderLocation(cloudModel.Materials[0].Shader, "viewPos");
    }

    private void PrepareSkyBox()
    {
        unsafe
        {
            Mesh cube = Raylib.GenMeshCube(1.0f, 1.0f, 1.0f);
            _skybox = Raylib.LoadModelFromMesh(cube);
            _skybox.Materials[0].Shader =
                Raylib.LoadShader("resources/shaders/skybox.vert", "resources/shaders/skybox.frag");
            _skyboxDaytimeLoc = Raylib.GetShaderLocation(_skybox.Materials[0].Shader, "daytime");
            _skyboxDayrotationLoc = Raylib.GetShaderLocation(_skybox.Materials[0].Shader, "dayrotation");
            _skyboxMoveFactor = 0.0f;
            _skyboxMoveFactorLoc = Raylib.GetShaderLocation(_skybox.Materials[0].Shader, "moveFactor");
            Shader shdrCubemap = Raylib.LoadShader("resources/shaders/cubemap.vert", "resources/shaders/cubemap.frag");

            Raylib.SetShaderValue(_skybox.Materials[0].Shader,
                Raylib.GetShaderLocation(_skybox.Materials[0].Shader, "environmentMapNight"),
                MaterialMapIndex.Cubemap,
                ShaderUniformDataType.Int);

            Raylib.SetShaderValue(_skybox.Materials[0].Shader,
                Raylib.GetShaderLocation(_skybox.Materials[0].Shader, "environmentMapDay"), MaterialMapIndex.Irradiance,
                ShaderUniformDataType.Int);

            Raylib.SetShaderValue(shdrCubemap, Raylib.GetShaderLocation(shdrCubemap, "equirectangularMap"), 0,
                ShaderUniformDataType.Int);
            Texture2D texHdr = Raylib.LoadTexture("resources/milkyWay.hdr"); // Load HDR panorama (sphere) texture
            Texture2D texHdr2 = Raylib.LoadTexture("resources/daytime.hdr"); // Load HDR panorama (sphere) texture
                                                                             // Generate cubemap (texture with 6 quads-cube-mapping) from panorama HDR texture
                                                                             // NOTE: New texture is generated rendering to texture, shader computes the sphere->cube coordinates mapping
            _skybox.Materials[0].Maps[0].Texture = Raylib.LoadTexture("resources/skyGradient.png");
            Raylib.SetTextureFilter(_skybox.Materials[0].Maps[0].Texture, TextureFilter.Bilinear);
            Raylib.SetTextureWrap(_skybox.Materials[0].Maps[0].Texture, TextureWrap.Clamp);

            //TODO check if pixel format is the right one
            _skybox.Materials[0].Maps[(int)MaterialMapIndex.Cubemap].Texture = GenTextureCubemap(shdrCubemap, texHdr, 1024, PixelFormat.UncompressedR8G8B8A8);
            _skybox.Materials[0].Maps[(int)MaterialMapIndex.Irradiance].Texture = GenTextureCubemap(shdrCubemap, texHdr2, 1024, PixelFormat.UncompressedR8G8B8A8);

            Raylib.SetTextureFilter(_skybox.Materials[0].Maps[(int)MaterialMapIndex.Cubemap].Texture, TextureFilter.Bilinear);
            Raylib.SetTextureFilter(_skybox.Materials[0].Maps[(int)MaterialMapIndex.Irradiance].Texture, TextureFilter.Bilinear);
            Raylib.GenTextureMipmaps(&_skybox.Materials[0].Maps[(int)MaterialMapIndex.Cubemap].Texture);
            Raylib.GenTextureMipmaps(&_skybox.Materials[0].Maps[(int)MaterialMapIndex.Irradiance].Texture);
            Raylib.UnloadTexture(texHdr); // Texture not required anymore, cubemap already generated
            Raylib.UnloadTexture(texHdr2); // Texture not required anymore, cubemap already generated
            Raylib.UnloadShader(shdrCubemap); // Unload cubemap generation shader, not required anymore
        }
    }

    private void PrepareTerrain()
    {
        unsafe
        {
            // TERRAIN
            Mesh terrainMesh = Raylib.GenMeshPlane(32, 32, 256, 256); // Generate terrain mesh (RAM and VRAM)
            _terrainGradient =
                Raylib.LoadTexture("resources/terrainGradient.png"); // color ramp of terrain (rock and grass)
                                                                     //SetTextureFilter(terrainGradient, TextureFilter.Bilinear);
            Raylib.SetTextureWrap(_terrainGradient, TextureWrap.Clamp);
            Raylib.GenTextureMipmaps(ref _terrainGradient);
            _terrainModel = Raylib.LoadModelFromMesh(terrainMesh); // Load model from generated mesh
            _terrainModel.Transform = Raymath.MatrixTranslate(0, -1.2f, 0);
            _terrainModel.Materials[0].Maps[0].Texture = _terrainGradient;
            _terrainModel.Materials[0].Maps[2].Texture = _heightmapTexture;
            _terrainModel.Materials[0].Shader =
                Raylib.LoadShader("resources/shaders/terrain.vert", "resources/shaders/terrain.frag");
            // Get some shader loactions
            _terrainModel.Materials[0].Shader.Locs[(int)ShaderLocationIndex.MatrixModel] =
                Raylib.GetShaderLocation(_terrainModel.Materials[0].Shader, "matModel");
            _terrainModel.Materials[0].Shader.Locs[(int)ShaderLocationIndex.VectorView] =
                Raylib.GetShaderLocation(_terrainModel.Materials[0].Shader, "viewPos");
            _terrainDaytimeLoc = Raylib.GetShaderLocation(_terrainModel.Materials[0].Shader, "daytime");
            var cs = _clipShaders.AddClipShader(_terrainModel.Materials[0].Shader); // register as clip shader for automatization of clipPlanes
            Raylib.SetShaderValue(_terrainModel.Materials[0].Shader, _clipShaders.clipShaderHeightLocs[cs], 0.0f, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_terrainModel.Materials[0].Shader, _clipShaders.clipShaderTypeLocs[cs], 2, ShaderUniformDataType.Int);

            // ambient light level
            _terrainAmbientLoc = Raylib.GetShaderLocation(_terrainModel.Materials[0].Shader, "ambient");
            Raylib.SetShaderValue(_terrainModel.Materials[0].Shader, _terrainAmbientLoc, _ambc, ShaderUniformDataType.Vec4);
            Texture2D rockNormalMap = Raylib.LoadTexture("resources/rockNormalMap.png"); // normal map
            Raylib.SetTextureFilter(rockNormalMap, TextureFilter.Bilinear);
            Raylib.GenTextureMipmaps(&rockNormalMap);
            _terrainModel.Materials[0].Shader.Locs[(int)ShaderLocationIndex.MapRoughness] =
                Raylib.GetShaderLocation(_terrainModel.Materials[0].Shader, "rockNormalMap");
            _terrainModel.Materials[0].Maps[(int)MaterialMapIndex.Roughness].Texture = rockNormalMap;
        }
    }


    // Generate cubemap texture from HDR texture
    private static unsafe Texture2D GenTextureCubemap(Shader shader, Texture2D panorama, int size, PixelFormat format)
    {
        Texture2D cubemap;

        // Disable backface culling to render inside the cube
        Rlgl.DisableBackfaceCulling();

        // STEP 1: Setup framebuffer
        //------------------------------------------------------------------------------------------
        uint rbo = Rlgl.LoadTextureDepth(size, size, true);
        cubemap.Id = Rlgl.LoadTextureCubemap(null, size, format, 1);

        uint fbo = Rlgl.LoadFramebuffer();
        Rlgl.FramebufferAttach(
            fbo,
            rbo,
            FramebufferAttachType.Depth,
            FramebufferAttachTextureType.Renderbuffer,
            0
        );
        Rlgl.FramebufferAttach(
            fbo,
            cubemap.Id,
            FramebufferAttachType.ColorChannel0,
            FramebufferAttachTextureType.CubemapPositiveX,
            0
        );

        // Check if framebuffer is complete with attachments (valid)
        if (Rlgl.FramebufferComplete(fbo))
        {
            Console.WriteLine($"FBO: [ID {fbo}] Framebuffer object created successfully");
        }
        //------------------------------------------------------------------------------------------

        // STEP 2: Draw to framebuffer
        //------------------------------------------------------------------------------------------
        // NOTE: Shader is used to convert HDR equirectangular environment map to cubemap equivalent (6 faces)
        Rlgl.EnableShader(shader.Id);

        // Define projection matrix and send it to shader
        Matrix4x4 matFboProjection = Raymath.MatrixPerspective(
            90.0f * Raylib.DEG2RAD,
            1.0f,
            Rlgl.CULL_DISTANCE_NEAR,
            Rlgl.CULL_DISTANCE_FAR
        );
        Rlgl.SetUniformMatrix(shader.Locs[(int)ShaderLocationIndex.MatrixProjection], matFboProjection);

        // Define view matrix for every side of the cubemap
        Matrix4x4[] fboViews = new[]
        {
            Raymath.MatrixLookAt(Vector3.Zero, new Vector3(-1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)),
            Raymath.MatrixLookAt(Vector3.Zero, new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)),
            Raymath.MatrixLookAt(Vector3.Zero, new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f)),
            Raymath.MatrixLookAt(Vector3.Zero, new Vector3(0.0f, -1.0f, 0.0f), new Vector3(0.0f, 0.0f, -1.0f)),
            Raymath.MatrixLookAt(Vector3.Zero, new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, -1.0f, 0.0f)),
            Raymath.MatrixLookAt(Vector3.Zero, new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, -1.0f, 0.0f)),
        };

        // Set viewport to current fbo dimensions
        Rlgl.Viewport(0, 0, size, size);

        // Activate and enable texture for drawing to cubemap faces
        Rlgl.ActiveTextureSlot(0);
        Rlgl.EnableTexture(panorama.Id);

        for (int i = 0; i < 6; i++)
        {
            // Set the view matrix for the current cube face
            Rlgl.SetUniformMatrix(shader.Locs[(int)ShaderLocationIndex.MatrixView], fboViews[i]);

            // Select the current cubemap face attachment for the fbo
            // WARNING: This function by default enables->attach->disables fbo!!!
            Rlgl.FramebufferAttach(
                fbo,
                cubemap.Id,
                FramebufferAttachType.ColorChannel0,
                FramebufferAttachTextureType.CubemapPositiveX + i,
                0
            );
            Rlgl.EnableFramebuffer(fbo);

            // Load and draw a cube, it uses the current enabled texture
            Rlgl.ClearScreenBuffers();
            Rlgl.LoadDrawCube();
        }
        //------------------------------------------------------------------------------------------

        // STEP 3: Unload framebuffer and reset state
        //------------------------------------------------------------------------------------------
        Rlgl.DisableShader();
        Rlgl.DisableTexture();
        Rlgl.DisableFramebuffer();

        // Unload framebuffer (and automatically attached depth texture/renderbuffer)
        Rlgl.UnloadFramebuffer(fbo);

        // Reset viewport dimensions to default
        Rlgl.Viewport(0, 0, Rlgl.GetFramebufferWidth(), Rlgl.GetFramebufferHeight());
        Rlgl.EnableBackfaceCulling();
        //------------------------------------------------------------------------------------------

        cubemap.Width = size;
        cubemap.Height = size;
        cubemap.Mipmaps = 1;
        cubemap.Format = format;

        return cubemap;
    }


}