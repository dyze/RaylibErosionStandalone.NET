using Raylib_cs;

namespace RaylibErosionStandalone;

class SkyBox
{
    private float _skyBoxSpeed = 0.0085f;


    public Model _skyboxModel;

    public float _skyboxMoveFactor;
    public int _skyboxMoveFactorLoc = -1;
    public int _skyboxDaytimeLoc = -1;
    public int _skyboxDayRotationLoc = -1;


    public Model PrepareSkyBoxStatic()
    {
        Raylib.TraceLog(TraceLogLevel.Info, "PrepareSkyBoxStatic...");

        var meshCube = Raylib.GenMeshCube(1.0f, 1.0f, 1.0f);
        _skyboxModel = Raylib.LoadModelFromMesh(meshCube);

        var shader = Raylib.LoadShader("resources/shaders/glsl330/skybox-static.vert",
            "resources/shaders/glsl330/skybox-static.frag");

        Raylib.SetShaderValue(shader,
            Raylib.GetShaderLocation(shader, "environmentMap"),
            MaterialMapIndex.Cubemap,
            ShaderUniformDataType.Int);

        Raylib.SetShaderValue(
            shader,
            Raylib.GetShaderLocation(shader, "flipMode"),
            2,
            ShaderUniformDataType.Int
        );

        Raylib.SetMaterialShader(ref _skyboxModel, 0, ref shader);

        var imgDay = Raylib.LoadImage("resources/Daylight Box UV.png");
        var cubeMapDay = Raylib.LoadTextureCubemap(imgDay, CubemapLayout.AutoDetect);
        Raylib.SetMaterialTexture(ref _skyboxModel, 0, MaterialMapIndex.Cubemap, ref cubeMapDay);
        Raylib.UnloadImage(imgDay); // Texture not required anymore, cubemap already generated

        Raylib.TraceLog(TraceLogLevel.Info, "PrepareSkyBoxStatic OK");

        return _skyboxModel;
    }

    public Model PrepareSkyBoxDayNightWithCubeMapShader()
    {
        Raylib.TraceLog(TraceLogLevel.Info, "PrepareSkyBoxDayNightWithCubeMapShader...");

        var meshCube = Raylib.GenMeshCube(1.0f, 1.0f, 1.0f);
        _skyboxModel = Raylib.LoadModelFromMesh(meshCube);

        var shader = Raylib.LoadShader("resources/shaders/glsl330/skybox-daynight.vert",
            "resources/shaders/glsl330/skybox-daynight.frag");

        Raylib.SetShaderValue(shader,
            Raylib.GetShaderLocation(shader, "environmentMapNight"),
            MaterialMapIndex.Cubemap,
            ShaderUniformDataType.Int);

        Raylib.SetShaderValue(shader,
            Raylib.GetShaderLocation(shader, "environmentMapDay"),
            MaterialMapIndex.Irradiance,
            ShaderUniformDataType.Int);

        Raylib.SetMaterialShader(ref _skyboxModel, 0, ref shader);

        _skyboxDaytimeLoc = Raylib.GetShaderLocation(shader, "daytime");
        _skyboxDayRotationLoc = Raylib.GetShaderLocation(shader, "dayrotation");
        _skyboxMoveFactorLoc = Raylib.GetShaderLocation(shader, "moveFactor");

        var skyGradientTexture = Raylib.LoadTexture("resources/skyGradient.png");
        Raylib.SetMaterialTexture(ref _skyboxModel, 0, MaterialMapIndex.Albedo, ref skyGradientTexture);
        Raylib.SetTextureFilter(skyGradientTexture, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(skyGradientTexture, TextureWrap.Clamp);

        var cubeMapShader = Raylib.LoadShader("resources/shaders/glsl330/cubemap.vert",
            "resources/shaders/glsl330/cubemap.frag");
        Raylib.SetShaderValue(cubeMapShader, Raylib.GetShaderLocation(cubeMapShader, "equirectangularMap"), 0,
            ShaderUniformDataType.Int);

        {
            var imgNight = Raylib.LoadTexture("resources/milkyWay.png");
            var cubeMapNight = Tools.GenTextureCubeMap(cubeMapShader, imgNight, 1024, PixelFormat.UncompressedR8G8B8A8);
            Raylib.SetMaterialTexture(ref _skyboxModel, 0, MaterialMapIndex.Cubemap, ref cubeMapNight);
            Raylib.SetTextureFilter(cubeMapNight, TextureFilter.Bilinear);
            Raylib.GenTextureMipmaps(ref cubeMapNight);
            Raylib.UnloadTexture(imgNight); // Texture not required anymore, cubemap already generated
        }

        {
            var imgDay = Raylib.LoadTexture("resources/daytime.png");
            var cubeMapDay = Tools.GenTextureCubeMap(cubeMapShader, imgDay, 1024, PixelFormat.UncompressedR8G8B8A8);
            Raylib.SetMaterialTexture(ref _skyboxModel, 0, MaterialMapIndex.Irradiance, ref cubeMapDay);
            Raylib.SetTextureFilter(cubeMapDay, TextureFilter.Bilinear);
            Raylib.GenTextureMipmaps(ref cubeMapDay);
            Raylib.UnloadTexture(imgDay); // Texture not required anymore, cubemap already generated
        }

        Raylib.UnloadShader(cubeMapShader);


        Raylib.TraceLog(TraceLogLevel.Info, "PrepareSkyBoxDayNightWithCubeMapShader OK");

        return _skyboxModel;
    }

    public Model PrepareSkyBoxDayNight()
    {
        Raylib.TraceLog(TraceLogLevel.Info, "PrepareSkyBoxDayNight...");

        var meshCube = Raylib.GenMeshCube(1.0f, 1.0f, 1.0f);
        _skyboxModel = Raylib.LoadModelFromMesh(meshCube);

        var shader = Raylib.LoadShader("resources/shaders/glsl330/skybox-daynight.vert",
            "resources/shaders/glsl330/skybox-daynight.frag");

        Raylib.SetShaderValue(shader,
            Raylib.GetShaderLocation(shader, "environmentMapNight"),
            MaterialMapIndex.Cubemap,
            ShaderUniformDataType.Int);

        Raylib.SetShaderValue(shader,
            Raylib.GetShaderLocation(shader, "environmentMapDay"),
            MaterialMapIndex.Irradiance,
            ShaderUniformDataType.Int);

        Raylib.SetMaterialShader(ref _skyboxModel, 0, ref shader);

        _skyboxDaytimeLoc = Raylib.GetShaderLocation(shader, "daytime");
        _skyboxDayRotationLoc = Raylib.GetShaderLocation(shader, "dayrotation");
        _skyboxMoveFactorLoc = Raylib.GetShaderLocation(shader, "moveFactor");

        var skyGradientTexture = Raylib.LoadTexture("resources/skyGradient.png");
        Raylib.SetMaterialTexture(ref _skyboxModel, 0, MaterialMapIndex.Albedo, ref skyGradientTexture);
        Raylib.SetTextureFilter(skyGradientTexture, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(skyGradientTexture, TextureWrap.Clamp);

        {
            var imgNight = Raylib.LoadImage("resources/night-sky.png");
            var cubeMapNight = Raylib.LoadTextureCubemap(imgNight, CubemapLayout.CrossFourByThree);
            Raylib.SetMaterialTexture(ref _skyboxModel, 0, MaterialMapIndex.Cubemap, ref cubeMapNight);
            Raylib.SetTextureFilter(cubeMapNight, TextureFilter.Bilinear);
            Raylib.GenTextureMipmaps(ref cubeMapNight);
            Raylib.UnloadImage(imgNight); // Texture not required anymore, cubemap already generated
        }

        {
            var imgDay = Raylib.LoadImage("resources/Daylight Box UV.png");
            var cubeMapDay = Raylib.LoadTextureCubemap(imgDay, CubemapLayout.AutoDetect);
            Raylib.SetMaterialTexture(ref _skyboxModel, 0, MaterialMapIndex.Irradiance, ref cubeMapDay);
            Raylib.SetTextureFilter(cubeMapDay, TextureFilter.Bilinear);
            Raylib.GenTextureMipmaps(ref cubeMapDay);
            Raylib.UnloadImage(imgDay); // Texture not required anymore, cubemap already generated
        }


        Raylib.TraceLog(TraceLogLevel.Info, "PrepareSkyBoxDayNight OK");

        return _skyboxModel;
    }

    public void AnimateSkyBox()
    {
        _skyboxMoveFactor += _skyBoxSpeed * Raylib.GetFrameTime();
        while (_skyboxMoveFactor > 1.0f)
            _skyboxMoveFactor -= 1.0f;
    }



    public void ApplySkyBoxMoveFactor()
    {
        var shader = Raylib.GetMaterial(ref _skyboxModel, 0).Shader;
        Raylib.SetShaderValue(shader,
            _skyboxMoveFactorLoc,
            _skyboxMoveFactor,
            ShaderUniformDataType.Float);
    }


}