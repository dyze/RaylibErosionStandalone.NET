using Raylib_cs;

namespace RaylibErosionStandalone;

class Clouds
{
    private readonly float _cloudSpeed = 0.0032f;

    public Model Model;

    private float _cloudMoveFactor;
    private int _cloudMoveFactorLoc = -1;
    public int _cloudDaytimeLoc = -1;


    public Model PrepareClouds()
    {
        Raylib.TraceLog(TraceLogLevel.Info, "PrepareClouds...");

        var cloudTexture = Raylib.LoadTexture("resources/clouds.png");
        Raylib.SetTextureFilter(cloudTexture, TextureFilter.Bilinear);
        Raylib.GenTextureMipmaps(ref cloudTexture);

        var cloudMesh = Raylib.GenMeshPlane(51200, 51200, 10, 10);
        Model = Raylib.LoadModelFromMesh(cloudMesh);
        Model.Transform = Raymath.MatrixTranslate(0, 100.0f, 0);

        Raylib.SetMaterialTexture(ref Model, 0, MaterialMapIndex.Albedo, ref cloudTexture);

        var shader = Raylib.LoadShader("resources/shaders/cirrostratus.vert",
            "resources/shaders/cirrostratus.frag");
        Raylib.SetMaterialShader(ref Model, 0, ref shader);

        _cloudMoveFactorLoc = Raylib.GetShaderLocation(shader, "moveFactor");
        _cloudDaytimeLoc = Raylib.GetShaderLocation(shader, "daytime");

        unsafe
        {
            Model.Materials[0].Shader.Locs[(int)ShaderLocationIndex.MatrixModel] =
                Raylib.GetShaderLocation(Model.Materials[0].Shader, "matModel");
            Model.Materials[0].Shader.Locs[(int)ShaderLocationIndex.VectorView] =
                Raylib.GetShaderLocation(Model.Materials[0].Shader, "viewPos");
        }

        Raylib.TraceLog(TraceLogLevel.Info, "PrepareClouds OK");

        return Model;
    }

    public void AnimateClouds()
    {
        _cloudMoveFactor += _cloudSpeed * Raylib.GetFrameTime();
        while (_cloudMoveFactor > 1.0f)
            _cloudMoveFactor -= 1.0f;
    }



    public void ApplyCloudMoveFactor()
    {
        var shader = Raylib.GetMaterial(ref Model, 0).Shader;
        Raylib.SetShaderValue(shader,
            _cloudMoveFactorLoc,
            _cloudMoveFactor,
            ShaderUniformDataType.Float);
    }
}