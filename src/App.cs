using System.ComponentModel;
using System.Numerics;
using Raylib_cs;
using RaylibErosionStandalone;
using rlImGui_cs;
using Image = Raylib_cs.Image;
using KeyboardKey = Raylib_cs.KeyboardKey;
using MouseButton = Raylib_cs.MouseButton;
using PixelFormat = Raylib_cs.PixelFormat;
using RenderTexture2D = Raylib_cs.RenderTexture2D;

namespace RaylibErosionStandalone;

class App
{
    private const int MAP_RESOLUTION = 512; // width and height of heightmap
    private const int CLIP_SHADERS_COUNT = 1; // number of shaders that use a clipPlane
    private const int TREE_TEXTURE_COUNT = 19; // number of textures for a tree
    private const int TREE_COUNT = 8190; // number of tree billboards

    private Shader treeShader; // shader used for tree billboards
    private int _treeAmbientLoc;

    private const int screenWidth = 1280; // initial size of window
    private const int screenHeight = 720;
    private const float FboSize = 2.5f; // Fame Buffer Object
    private int windowWidthBeforeFullscreen = screenWidth;
    private int windowHeightBeforeFullscreen = screenWidth;
    private bool windowSizeChanged = false; // set to true when switching to fullscreen

    private List<Vector2> displayResolutions =
    [
        new(320, 180), // 0
        new(640, 36), // 1
        new(1280, 720), // 2
        new(1600, 900), // 3
        new(1920, 1080) // 4
    ];

    private int currentDisplayResolutionIndex = 2;

    private bool useApplicationBuffer = false; // wether to use app buffer or not
    private bool lockTo60FPS = false;

    private float daytime = 0.2f; // range (0, 1) but is sent to shader as a range(-1, 1) normalized upon a unit sphere
    private float dayspeed = 0.015f;
    private bool dayrunning = true; // if day is animating

    private List<float> ambc =
    [
        0.22f, 0.17f, 0.41f, 0.2f
    ]; // current ambient color & intensity

    private List<TreeBillboard> _noTrees; // keep empty
    private List<TreeBillboard> _trees; // fill with tree data

    private int totalDroplets = 0; // total amount of droplets simulated
    private int dropletsSinceLastTreeRegen = 0; // used to regenerate trees after certain droplets have fallen

    private Shader postProcessShader;
    private RenderTexture2D _applicationBuffer;
    private RenderTexture2D _reflectionBuffer;
    private RenderTexture2D _refractionBuffer;

    private float angle = 6.282f;
    private float radius = 100.0f;

    private Model _terrainModel;

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

    private Model _skybox;

    private Camera3D _camera = new();

    private List<Light> _lights = [];

    private ErosionMaker _erosionMaker;

    private static ClipShaders _clipShaders;

    private List<float> _mapData;
    private Texture2D _heightmapTexture;

    private List<Vector4> ambientColors;

    public void Run()
    {
        Image ambientColorsImage = Raylib.LoadImage("resources/ambientGradient.png");
        ambientColors = GetImageDataNormalized(ambientColorsImage); // array of colors for ambient color through the day
        int ambientColorsNumber = ambientColorsImage.Width; // length of array
        Raylib.UnloadImage(ambientColorsImage);

        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint |
                              ConfigFlags.ResizableWindow); // Enable Multi Sampling Anti Aliasing 4x (if available)

        Raylib.InitWindow(screenWidth, screenHeight, "Terrain Erosion (.NET)");
        rlImGui.Setup();

        postProcessShader = Raylib.LoadShader("", "resources/shaders/postprocess.frag");
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
            Raylib.GenImagePerlinNoise(MAP_RESOLUTION, MAP_RESOLUTION, 50, 50, 4.0f); // generate fractal perlin noise
        _mapData = new List<float>(MAP_RESOLUTION * MAP_RESOLUTION);
        // Extract pixels and put them in mapData
        Color* pixels = Raylib.GetImageData(initialHeightmapImage);
        for (var i = 0; i < MAP_RESOLUTION * MAP_RESOLUTION; i++)
        {
            _mapData[i] = pixels[i].R / 255.0f;
        }

        Erode();


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
            if (useApplicationBuffer) Raylib.EndTextureMode();

            // render to frame buffer after applying post-processing (if enabled)
            if (useApplicationBuffer)
            {
                Raylib.BeginShaderMode(postProcessShader);
                // NOTE: Render texture must be y-flipped due to default OpenGL coordinates (left-bottom)
                Raylib.DrawTextureRec(_applicationBuffer.Texture,
                    new Rectangle(
                        0.0f, 0.0f, (float)_applicationBuffer.Texture.Width, (float)-_applicationBuffer.Texture.Height
                    ),
                    new Vector2(
                        0.0f, 0.0f
                    ),
                    Color.White);
                Raylib.EndShaderMode();
            }

            float hour = daytime * 24.0f;
            float minute = (daytime * 24.0f - hour) * 60.0f;
            // render GUI
            if (!Raylib.IsKeyDown(KeyboardKey.F6))
            {
                if (!Raylib.IsKeyDown(KeyboardKey.F1))
                {
                    Raylib.DrawText("Hold F1 to display controls. Hold ALT to enable cursor.", 10, 10, 20, Color.White);
                    Raylib.DrawText(Raylib.TextFormat("Droplets simulated: %i", totalDroplets), 10, 40, 20,
                        Color.White);
                    Raylib.DrawText(Raylib.TextFormat("FPS: %2i", Raylib.GetFPS()), 10, 70, 20, Color.White);

                    Raylib.DrawText(Raylib.TextFormat("%02d : %02d", hour, minute), Raylib.GetScreenWidth() - 80, 10,
                        20,
                        Color.White);
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
                _erosionMaker.Erode(ref _mapData, MAP_RESOLUTION, spd, false);
                totalDroplets += spd;
                dropletsSinceLastTreeRegen += spd;

                // Update pixels
                for (var i = 0; i < MAP_RESOLUTION * MAP_RESOLUTION; i++)
                {
                    byte val = (byte)(_mapData[i] * 255);
                    pixels[i].R = val;
                    pixels[i].G = val;
                    pixels[i].B = val;
                    pixels[i].A = 255;
                }

                Raylib.UnloadTexture(_heightmapTexture);
                Image heightmapImage = Raylib.LoadImageEx(pixels, MAP_RESOLUTION, MAP_RESOLUTION);
                _heightmapTexture = Raylib.LoadTextureFromImage(heightmapImage); // Convert image to texture (VRAM)
                Raylib.SetTextureFilter(_heightmapTexture, TextureFilter.Bilinear);
                Raylib.SetTextureWrap(_heightmapTexture, TextureWrap.Clamp);
                _terrainModel.Materials[0].Maps[2].Texture = _heightmapTexture;
                Raylib.UnloadImage(heightmapImage); // Unload heightmap image from RAM, already uploaded to VRAM

                if (dropletsSinceLastTreeRegen > spd * 10)
                {
                    GenerateTrees(_erosionMaker, ref _mapData, _treeTextures, _trees, false);
                    dropletsSinceLastTreeRegen = 0;
                }
            }

            if (Raylib.IsKeyPressed(KeyboardKey.X))
            {
                unsafe
                {
                    // Erode
                    std::chrono::steady_clock::time_point begin = std::chrono::steady_clock::now();
                    _erosionMaker.Erode(ref _mapData, MAP_RESOLUTION, 100000, false);
                    std::chrono::steady_clock::time_point end = std::chrono::steady_clock::now();

                    Raylib.SetTraceLogLevel(TraceLogLevel.Info);
                    Raylib.TraceLog(TraceLogLevel.Info,
                        Raylib.TextFormat("Eroded 100000 droplets. Time elapsed: %f s",
                            std::chrono::duration_cast<std::chrono::nanoseconds>(end - begin).count() / 1000000000.0));
                    Raylib.SetTraceLogLevel(TraceLogLevel.None);

                    totalDroplets += 100000;
                    // Update pixels
                    for (var i = 0; i < MAP_RESOLUTION * MAP_RESOLUTION; i++)
                    {
                        byte val = (byte)(_mapData[i] * 255);
                        pixels[i].R = val;
                        pixels[i].G = val;
                        pixels[i].B = val;
                        pixels[i].A = 255;
                    }

                    Raylib.UnloadTexture(_heightmapTexture);
                    Image heightmapImage = Raylib.LoadImageEx(pixels, MAP_RESOLUTION, MAP_RESOLUTION);
                    _heightmapTexture = Raylib.LoadTextureFromImage(heightmapImage); // Convert image to texture (VRAM)
                    Raylib.SetTextureFilter(_heightmapTexture, TextureFilter.Bilinear);
                    Raylib.SetTextureWrap(_heightmapTexture, TextureWrap.Clamp);
                    _terrainModel.Materials[0].Maps[2].Texture = _heightmapTexture;
                    Raylib.UnloadImage(heightmapImage); // Unload heightmap image from RAM, already uploaded to VRAM

                    GenerateTrees(_erosionMaker, ref _mapData, _treeTextures, ref _trees, false);
                    dropletsSinceLastTreeRegen = 0;
                }
            }


            if (Raylib.IsKeyPressed(KeyboardKey.R) || Raylib.IsKeyPressed(KeyboardKey.T) ||
                Raylib.IsKeyPressed(KeyboardKey.Y) ||
                Raylib.IsKeyPressed(KeyboardKey.U))
            {
                totalDroplets = 0;
                pixels = GetImageData(initialHeightmapImage);
                for (var i = 0; i < MAP_RESOLUTION * MAP_RESOLUTION; i++)
                {
                    _mapData[i] = pixels[i].R / 255.0f;
                }

                // reinit map
                if (Raylib.IsKeyPressed(KeyboardKey.R))
                {
                    _erosionMaker.Gradient(ref _mapData, MAP_RESOLUTION, 0.5f, ErosionMaker.GradientType.SQUARE);
                }
                else if (Raylib.IsKeyPressed(KeyboardKey.T))
                {
                    _erosionMaker.Gradient(ref _mapData, MAP_RESOLUTION, 0.5f, ErosionMaker.GradientType.CIRCLE);
                }
                else if (Raylib.IsKeyPressed(KeyboardKey.Y))
                {
                    _erosionMaker.Gradient(ref _mapData, MAP_RESOLUTION, 0.5f, ErosionMaker.GradientType.DIAMOND);
                }
                else if (Raylib.IsKeyPressed(KeyboardKey.U))
                {
                    _erosionMaker.Gradient(ref _mapData, MAP_RESOLUTION, 0.5f, ErosionMaker.GradientType.STAR);
                }

                _erosionMaker.Remap(ref _mapData, MAP_RESOLUTION); // flatten beaches
                // no need to reinitialize erosion
                // Update pixels
                for (var i = 0; i < MAP_RESOLUTION * MAP_RESOLUTION; i++)
                {
                    byte val = (byte)(_mapData[i] * 255);
                    pixels[i].R = val;
                    pixels[i].G = val;
                    pixels[i].B = val;
                    pixels[i].A = 255;
                }

                Raylib.UnloadTexture(_heightmapTexture);
                Image heightmapImage = LoadImageEx(pixels, MAP_RESOLUTION, MAP_RESOLUTION);
                _heightmapTexture = LoadTextureFromImage(heightmapImage); // Convert image to texture (VRAM)
                Raylib.SetTextureFilter(_heightmapTexture, TextureFilter.Bilinear);
                Raylib.SetTextureWrap(_heightmapTexture, TextureWrap.Clamp);
                _terrainModel.Materials[0].Maps[2].Texture = _heightmapTexture;
                Raylib.UnloadImage(heightmapImage); // Unload heightmap image from RAM, already uploaded to VRAM

                GenerateTrees(_erosionMaker, _mapData, _treeTextures, _trees, false);
                dropletsSinceLastTreeRegen = 0;
            }

            if (Raylib.IsKeyDown(KeyboardKey.S))
            {
                // display FBOS for debug
                Raylib.DrawTextureRec(_reflectionBuffer.texture,  {
                    0.0f, 0.0f, (float)_reflectionBuffer.Texture.width, (float)-reflectionBuffer.Texture.height
                }, {
                    0.0f, 0.0f
                }, WHITE);
                Raylib.DrawTextureRec(_refractionBuffer.texture,  {
                    0.0f, 0.0f, (float)_refractionBuffer.Texture.width, (float)-refractionBuffer.Texture.height
                }, {
                    0.0f, (float)_reflectionBuffer.Texture.height
                }, WHITE);
            }

            if (Raylib.IsKeyDown(KeyboardKey.A))
            {
                // display other info for debug
                Raylib.DrawTextureEx(_heightmapTexture,  {
                    Raylib.GetScreenWidth() - _heightmapTexture.Width - 20.0f, 20
                },
                0, 1,
                Color.White);
                Raylib.DrawRectangleLines(Raylib.GetScreenWidth() - _heightmapTexture.Width - 20, 20,
                    _heightmapTexture.Width,
                    _heightmapTexture.Height,
                    Color.Green);

                //DrawFPS(10, 70);
            }

            if (Raylib.IsKeyPressed(KeyboardKey.LeftControl))
            {
                dayrunning = !dayrunning;
            }

            if (Raylib.IsKeyPressed(KeyboardKey.F2))
            {
                if (lockTo60FPS)
                {
                    lockTo60FPS = false;
                    Raylib.SetTargetFPS(0);
                }
                else
                {
                    lockTo60FPS = true;
                    Raylib.SetTargetFPS(60);
                }
            }

            if (Raylib.IsKeyPressed(KeyboardKey.F3))
            {
                currentDisplayResolutionIndex++;
                if (currentDisplayResolutionIndex > 4)
                    currentDisplayResolutionIndex = 0;

                windowSizeChanged = true;
                Raylib.SetWindowSize(displayResolutions[currentDisplayResolutionIndex].X,
                    displayResolutions[currentDisplayResolutionIndex].Y);
                Raylib.SetWindowPosition((Raylib.GetMonitorWidth(0) - Raylib.GetScreenWidth()) / 2,
                    (Raylib.GetMonitorHeight(0) - Raylib.GetScreenHeight()) / 2);
            }

            if (Raylib.IsKeyPressed(KeyboardKey.F4))
            {
                windowSizeChanged = true;
                if (!Raylib.IsWindowFullscreen())
                {
                    windowWidthBeforeFullscreen = Raylib.GetScreenWidth();
                    windowHeightBeforeFullscreen = Raylib.GetScreenHeight();
                    Raylib.SetWindowSize(Raylib.GetMonitorWidth(0), Raylib.GetMonitorHeight(0));
                }
                else
                {
                    Raylib.SetWindowSize(windowWidthBeforeFullscreen, windowHeightBeforeFullscreen);
                }

                Raylib.ToggleFullscreen();
            }


            if (Raylib.IsKeyPressed(KeyboardKey.F5))
            {
                useApplicationBuffer = !useApplicationBuffer;
            }

            if (Raylib.IsKeyPressed((KeyboardKey.F9)))
            {
                // take a screenshot
                for (int i = 0; i < INT_MAX; i++)
                {
                    const char* fileName = Raylib.TextFormat("screen%i.png", i);
                    if (Raylib.FileExists(fileName) == 0)
                    {
                        Raylib.TakeScreenshot(fileName);
                        break;
                    }
                }
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
        // Make the light orbit
        _lights[0].Position.X = cosf(sunAngle) * radius;
        _lights[0].Position.Y = sinf(sunAngle) * radius;
        _lights[0].Position.Z = std::max(sinf(sunAngle) * radius * 0.9f, -radius / 4.0f); // skew sun orbit

        UpdateLightValues(lights[0]);

        // Update the light shader with the camera view position
        float cameraPos[3] =  {
            _camera.Position.x, _camera.Position.y, _camera.Position.z
        }
        ;
        Raylib.SetShaderValue(_terrainModel.Materials[0].Shader,
            _terrainModel.Materials[0].Shader.Locs[LOC_VECTOR_VIEW],
            cameraPos, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_oceanModel.Materials[0].Shader, _oceanModel.Materials[0].Shader.Locs[LOC_VECTOR_VIEW],
            cameraPos,
            ShaderUniformDataType.Vec3);
    }

    private void AnimateDayTime()
    {
        unsafe
        {
            if (dayrunning)
            {
                daytime += dayspeed * Raylib.GetFrameTime();
                while (daytime > 1.0f)
                {
                    daytime -= 1.0f;
                }
            }

            if (Raylib.IsKeyDown(KeyboardKey.Space))
            {
                daytime += dayspeed * (5.0f - (float)dayrunning) * Raylib.GetFrameTime();
                while (daytime > 1.0f)
                {
                    daytime -= 1.0f;
                }
            }

            float sunAngle = Lerp(-90, 270, daytime) * Raylib.DEG2RAD; // -90 midnight, 90 midday
            float
                nDaytime = sinf(
                    sunAngle); // normalize it to make it look like a dot product on an unit sphere (shaders expect it this way) (-1, 1)
            int iDaytime = ((nDaytime + 1.0f) / 2.0f) * (float)(ambientColorsNumber - 1);
            ambc[0] = _ambientColors[iDaytime].x; // ambient color based on daytime
            ambc[1] = ambientColors[iDaytime].y;
            ambc[2] = ambientColors[iDaytime].z;
            ambc[3] = Lerp(0.05f, 0.25f, ((nDaytime + 1.0f) / 2.0f)); // ambient strength based on daytime
            Raylib.SetShaderValue(_terrainModel.Materials[0].Shader, _terrainDaytimeLoc, &nDaytime,
                ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_skybox.Materials[0].Shader, _skyboxDaytimeLoc, &nDaytime,
                ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_skybox.Materials[0].Shader, _skyboxDayrotationLoc, &daytime,
                ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_cloudModel.Materials[0].Shader, _cloudDaytimeLoc, &nDaytime,
                ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_terrainModel.Materials[0].Shader, _terrainAmbientLoc, ambc,
                ShaderUniformDataType.Vec4);
            Raylib.SetShaderValue(treeShader, _treeAmbientLoc, ambc, ShaderUniformDataType.Vec4);
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

        Raylib.SetShaderValue(treeShader, _treeMoveFactorLoc, _treeMoveFactor, ShaderUniformDataType.Float);
    }

    private void HandleWindowResize()
    {
        unsafe
        {
            windowSizeChanged = false;
            // resize fbos based on screen size
            Raylib.UnloadRenderTexture(_applicationBuffer);
            Raylib.UnloadRenderTexture(_reflectionBuffer);
            Raylib.UnloadRenderTexture(_refractionBuffer);
            _applicationBuffer =
                Raylib.LoadRenderTexture(Raylib.GetScreenWidth(),
                    Raylib.GetScreenHeight()); // main FBO used for postprocessing
            _reflectionBuffer =
                Raylib.LoadRenderTexture(Raylib.GetScreenWidth() / FboSize,
                    Raylib.GetScreenHeight() / FboSize); // FBO used for water reflection
            _refractionBuffer =
                Raylib.LoadRenderTexture(Raylib.GetScreenWidth() / FboSize,
                    Raylib.GetScreenHeight() / FboSize); // FBO used for water refraction
            Raylib.SetTextureFilter(_reflectionBuffer.Texture, TextureFilter.Bilinear);
            Raylib.SetTextureFilter(_refractionBuffer.Texture, TextureFilter.Bilinear);

            // to be sure
            _oceanModel.Materials[0].Maps[0].Texture = _reflectionBuffer.Texture; // uniform texture0
            _oceanModel.Materials[0].Maps[1].Texture = _refractionBuffer.Texture; // uniform texture1

            Raylib.SetTraceLogLevel(TraceLogLevel.Info);
            Raylib.TraceLog(TraceLogLevel.Info,
                Raylib.TextFormat("Window resized: %d x %d", Raylib.GetScreenWidth(), Raylib.GetScreenHeight()));
            Raylib.SetTraceLogLevel(TraceLogLevel.None);
        }
    }

    private unsafe void Erode()
    {
        // Erode
        _erosionMaker.Gradient(ref _mapData, MAP_RESOLUTION, 0.5f,
            ErosionMaker.GradientType
                .SQUARE); // apply a centered gradient to smooth out border pixel (create island at center)
        _erosionMaker.Remap(ref _mapData, MAP_RESOLUTION); // flatten beaches
        _erosionMaker.Erode(ref _mapData, MAP_RESOLUTION, 0, true); // Erode (0 droplets for initialization)
        // Update pixels from mapData to texture
        for (size_t i = 0; i < MAP_RESOLUTION * MAP_RESOLUTION; i++)
        {
            int val = _mapData->at(i) * 255;
            pixels[i].r = val;
            pixels[i].g = val;
            pixels[i].b = val;
            pixels[i].a = 255;
        }

        Image heightmapImage = Raylib.LoadImageEx(pixels, MAP_RESOLUTION, MAP_RESOLUTION);
        _heightmapTexture = Raylib.LoadTextureFromImage(heightmapImage); // Convert image to texture (VRAM)
        Raylib.UnloadImage(heightmapImage); // Unload heightmap image from RAM, already uploaded to VRAM
        Raylib.SetTextureFilter(_heightmapTexture, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(_heightmapTexture, TextureWrap.Clamp);
        Raylib.GenTextureMipmaps(ref _heightmapTexture);
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
                    _terrainModel.Materials[0].Shader, _oceanModel.Materials[0].Shader, treeShader,
                    _skybox.Materials[0].Shader
                ]
            ));
        }
    }

    private void PrepareTrees()
    {
        for (var i = 0; i < TREE_TEXTURE_COUNT; i++)
        {
            _treeTextures[i] =
                Raylib.LoadTexture(Raylib.TextFormat("resources/trees/b/%i.png",
                    i)); // variant b of trees looks much better
            Raylib.SetTextureFilter(_treeTextures[i], TextureFilter.Bilinear);
            //GenTextureMipmaps(&treeTextures[i]); // looks better without
        }

        GenerateTrees(_erosionMaker, ref _mapData, _treeTextures, _trees, true);
        Material treeMaterial = Raylib.LoadMaterialDefault();
        treeShader = Raylib.LoadShader("resources/shaders/vegetation.vert", "resources/shaders/vegetation.frag");
        treeShader.Locs[LOC_MATRIX_MODEL] = Raylib.GetShaderLocation(treeShader, "matModel");
        treeAmbientLoc = Raylib.GetShaderLocation(treeShader, "ambient");
        Raylib.SetShaderValue(treeShader, treeAmbientLoc, ambc, ShaderUniformDataType.Vec4);
        treeMaterial.Shader = treeShader;
        treeMaterial.Maps[1].Texture = DUDVTex;
        _treeMoveFactor = 0.0f;
        _treeMoveFactorLoc = Raylib.GetShaderLocation(treeShader, "moveFactor");
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
        for (var i = 0; i < CLIP_SHADERS_COUNT; i++) // setup clip plane for shaders that use it
        {
            Raylib.SetShaderValue(_clipShaders[i], _clipShaderTypeLocs[i], &clipPlane, ShaderUniformDataType.Int);
        }

        for (var i = 0; i < models.Count(); i++) // draw all 3d models in the scene
        {
            Raylib.DrawModel(models[i], Vector3.Zero, 1.0f, Color.White);
        }

        Raylib.BeginShaderMode(treeShader);
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

        for (var i = 0; i < TREE_COUNT; i++) // 8190 max billboards, more than that and they are not cached anymore
        {
            int px, py;
            do
            {
                // try to generate a billboard
                billPosition.x = randomRange(-16, 16);
                billPosition.z = randomRange(-16, 16);
                px = ((billPosition.x + 16.0f) / 32.0f) * (MAP_RESOLUTION - 1);
                py = ((billPosition.z + 16.0f) / 32.0f) * (MAP_RESOLUTION - 1);
                billNormal = erosionMaker->GetNormal(mapData, MAP_RESOLUTION, px, py);
                billPosition.y = mapData->at(py * MAP_RESOLUTION + px) * 8 - 1.1f;

                float slope = 1.0 - billNormal.y;
                float grassBlendHeight = grassSlopeThreshold * (1.0 - grassBlendAmount);
                grassWeight = 1.0 -
                              std::min(
                                  std::max((slope - grassBlendHeight) / (grassSlopeThreshold - grassBlendHeight), 0.0f),
                                  1.0f);
            } while (billPosition.y < 0.32f || billPosition.y > 3.25f ||
                     grassWeight < 0.65f); // repeat until you find valid parameters (height and normal of chosen spot)

            billColor.r = (billNormal.x + 1) * 127.5f; // terrain normal where tree is located, stored on color
            billColor.g = (billNormal.y + 1) * 127.5f; // convert from range (-1, 1) to (0, 255)
            billColor.b = (billNormal.z + 1) * 127.5f;

            if (!generateNew)
            {
                (*trees)[i].position = billPosition;
                (*trees)[i].color = billColor;
            }
            else
            {
                int textureChoice = (int)randomRange(0, TREE_TEXTURE_COUNT);
                trees->push_back({
                    treeTextures[textureChoice], billPosition, randomRange(0.6f, 1.4f) * 0.3f, billColor
                });
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
        Texture2D DUDVTex = Raylib.LoadTexture("resources/waterDUDV.png");
        Raylib.SetTextureFilter(DUDVTex, TextureFilter.Bilinear);
        Raylib.GenTextureMipmaps(&DUDVTex);
        oceanModel.Transform = Raymath.MatrixTranslate(0, 0, 0);
        oceanModel.Materials[0].Maps[0].Texture = _reflectionBuffer.Texture; // uniform texture0
        oceanModel.Materials[0].Maps[1].Texture = _refractionBuffer.Texture; // uniform texture1
        oceanModel.Materials[0].Maps[2].Texture = DUDVTex; // uniform texture2
        oceanModel.Materials[0].Shader =
            Raylib.LoadShader("resources/shaders/water.vert", "resources/shaders/water.frag");
        _waterMoveFactor = 0.0f;
        _waterMoveFactorLoc = Raylib.GetShaderLocation(oceanModel.Materials[0].Shader, "moveFactor");
        oceanModel.Materials[0].Shader.Locs[LOC_MATRIX_MODEL] =
            Raylib.GetShaderLocation(oceanModel.Materials[0].Shader, "matModel");
        oceanModel.Materials[0].Shader.Locs[LOC_VECTOR_VIEW] =
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
        cloudModel.Materials[0].Shader.Locs[LOC_MATRIX_MODEL] =
            Raylib.GetShaderLocation(cloudModel.Materials[0].Shader, "matModel");
        cloudModel.Materials[0].Shader.Locs[LOC_VECTOR_VIEW] =
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
            int param[1] =  {
                MAP_CUBEMAP
            }
            ;
            Raylib.SetShaderValue(_skybox.Materials[0].Shader,
                Raylib.GetShaderLocation(_skybox.Materials[0].Shader, "environmentMapNight"), param, UNIFORM_INT);
            int param2[1] =  {
                MAP_IRRADIANCE
            }
            ;
            Raylib.SetShaderValue(_skybox.Materials[0].Shader,
                Raylib.GetShaderLocation(_skybox.Materials[0].Shader, "environmentMapDay"), param2,
                ShaderUniformDataType.Int);
            int param3[1] =  {
                0
            }
            ;
            Raylib.SetShaderValue(shdrCubemap, Raylib.GetShaderLocation(shdrCubemap, "equirectangularMap"), param3,
                ShaderUniformDataType.Int);
            Texture2D texHDR = Raylib.LoadTexture("resources/milkyWay.hdr"); // Load HDR panorama (sphere) texture
            Texture2D texHDR2 = Raylib.LoadTexture("resources/daytime.hdr"); // Load HDR panorama (sphere) texture
            // Generate cubemap (texture with 6 quads-cube-mapping) from panorama HDR texture
            // NOTE: New texture is generated rendering to texture, shader computes the sphere->cube coordinates mapping
            _skybox.Materials[0].Maps[0].Texture = Raylib.LoadTexture("resources/skyGradient.png");
            Raylib.SetTextureFilter(_skybox.Materials[0].Maps[0].Texture, TextureFilter.Bilinear);
            Raylib.SetTextureWrap(_skybox.Materials[0].Maps[0].Texture, TextureWrap.Clamp);
            _skybox.Materials[0].Maps[MAP_CUBEMAP].Texture = GenTextureCubemap(shdrCubemap, texHDR, 1024);
            _skybox.Materials[0].Maps[MAP_IRRADIANCE].Texture = GenTextureCubemap(shdrCubemap, texHDR2, 1024);
            Raylib.SetTextureFilter(_skybox.Materials[0].Maps[MAP_CUBEMAP].Texture, TextureFilter.Bilinear);
            Raylib.SetTextureFilter(_skybox.Materials[0].Maps[MAP_IRRADIANCE].Texture, TextureFilter.Bilinear);
            Raylib.GenTextureMipmaps(&_skybox.Materials[0].Maps[MAP_CUBEMAP].Texture);
            Raylib.GenTextureMipmaps(&_skybox.Materials[0].Maps[MAP_IRRADIANCE].Texture);
            Raylib.UnloadTexture(texHDR); // Texture not required anymore, cubemap already generated
            Raylib.UnloadTexture(texHDR2); // Texture not required anymore, cubemap already generated
            Raylib.UnloadShader(shdrCubemap); // Unload cubemap generation shader, not required anymore
        }
    }

    private void PrepareTerrain()
    {
        unsafe
        {
            // TERRAIN
            Mesh terrainMesh = Raylib.GenMeshPlane(32, 32, 256, 256); // Generate terrain mesh (RAM and VRAM)
            Texture2D terrainGradient =
                Raylib.LoadTexture("resources/terrainGradient.png"); // color ramp of terrain (rock and grass)
            //SetTextureFilter(terrainGradient, TextureFilter.Bilinear);
            Raylib.SetTextureWrap(terrainGradient, TextureWrap.Clamp);
            Raylib.GenTextureMipmaps(&terrainGradient);
            _terrainModel = Raylib.LoadModelFromMesh(terrainMesh); // Load model from generated mesh
            _terrainModel.Transform = Raymath.MatrixTranslate(0, -1.2f, 0);
            _terrainModel.Materials[0].Maps[0].Texture = terrainGradient;
            _terrainModel.Materials[0].Maps[2].Texture = _heightmapTexture;
            _terrainModel.Materials[0].Shader =
                Raylib.LoadShader("resources/shaders/terrain.vert", "resources/shaders/terrain.frag");
            // Get some shader loactions
            _terrainModel.Materials[0].Shader.Locs[LOC_MATRIX_MODEL] =
                Raylib.GetShaderLocation(_terrainModel.Materials[0].Shader, "matModel");
            _terrainModel.Materials[0].Shader.Locs[LOC_VECTOR_VIEW] =
                Raylib.GetShaderLocation(_terrainModel.Materials[0].Shader, "viewPos");
            int terrainDaytimeLoc = Raylib.GetShaderLocation(_terrainModel.Materials[0].Shader, "daytime");
            int cs = _clipShaders.AddClipShader(_terrainModel.Materials[0].Shader); // register as clip shader for automatization of clipPlanes
            float param10 = 0.0f;
            int param11 = 2;
            Raylib.SetShaderValue(_terrainModel.Materials[0].Shader, _clipShaders.clipShaderHeightLocs[cs], &param10, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_terrainModel.Materials[0].Shader, _clipShaders.clipShaderTypeLocs[cs], &param11, ShaderUniformDataType.Int);
            // ambient light level
            int terrainAmbientLoc = Raylib.GetShaderLocation(terrainModel.Materials[0].Shader, "ambient");
            Raylib.SetShaderValue(_terrainModel.Materials[0].Shader, terrainAmbientLoc, ambc, ShaderUniformDataType.Vec4);
            Texture2D rockNormalMap = Raylib.LoadTexture("resources/rockNormalMap.png"); // normal map
            Raylib.SetTextureFilter(rockNormalMap, TextureFilter.Bilinear);
            Raylib.GenTextureMipmaps(&rockNormalMap);
            _terrainModel.Materials[0].Shader.Locs[LOC_MAP_ROUGHNESS] =
                Raylib.GetShaderLocation(_terrainModel.Materials[0].Shader, "rockNormalMap");
            _terrainModel.Materials[0].Maps[MAP_ROUGHNESS].Texture = rockNormalMap;
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