using ImGuiNET;
using Raylib_cs;

namespace RaylibErosionStandalone;

class Ocean
{
    private float _treeSpeed = 0.125f;


    public float _oceanSpeed = 0.03f;
    public int _oceanMoveFactorLoc = -1;
    public float _oceanMoveFactor;

    public Model Model;

    public Model PrepareOcean(Texture2D reflectionTexture,
     Texture2D refractionTexture)
    {
        Raylib.TraceLog(TraceLogLevel.Info, "PrepareOcean...");

        var oceanMesh = Raylib.GenMeshPlane(5120, 5120, 10, 10);
        Model = Raylib.LoadModelFromMesh(oceanMesh);

        var _duDvMap = Raylib.LoadTexture("resources/waterDUDV.png");
        Raylib.SetTextureFilter(_duDvMap, TextureFilter.Bilinear);
        Raylib.GenTextureMipmaps(ref _duDvMap);

        Model.Transform = Raymath.MatrixTranslate(0, 0, 0);

        Raylib.SetMaterialTexture(ref Model, 0, MaterialMapIndex.Albedo, ref reflectionTexture);
        Raylib.SetMaterialTexture(ref Model, 0, MaterialMapIndex.Metalness, ref refractionTexture);
        Raylib.SetMaterialTexture(ref Model, 0, MaterialMapIndex.Normal, ref _duDvMap);

        var shader = Raylib.LoadShader("resources/shaders/ocean.vert", "resources/shaders/ocean.frag");
        Raylib.SetMaterialShader(ref Model, 0, ref shader);

        _oceanMoveFactorLoc = Raylib.GetShaderLocation(shader, "moveFactor");

        unsafe
        {
            Model.Materials[0].Shader.Locs[(int)ShaderLocationIndex.MatrixModel] =
                Raylib.GetShaderLocation(Model.Materials[0].Shader, "matModel");
            Model.Materials[0].Shader.Locs[(int)ShaderLocationIndex.VectorView] =
                Raylib.GetShaderLocation(Model.Materials[0].Shader, "viewPos");
        }

        Raylib.TraceLog(TraceLogLevel.Info, "PrepareOcean OK");

        return Model;
    }

    public void AnimateOcean()
    {
        // animate water
        _oceanMoveFactor += _oceanSpeed * Raylib.GetFrameTime();
        while (_oceanMoveFactor > 1.0f)
            _oceanMoveFactor -= 1.0f;
    }

    public void ApplyOceanMoveFactor()
    {
        var shader = Raylib.GetMaterial(ref Model, 0).Shader;
        Raylib.SetShaderValue(shader, _oceanMoveFactorLoc, _oceanMoveFactor, ShaderUniformDataType.Float);
    }


    public void RenderOceanValues()
    {
        if (ImGui.Begin("Ocean"))
        {
            ImGui.SliderFloat("_oceanMoveFactor", ref _oceanMoveFactor, 0f, 1f);
            ImGui.SliderFloat("_oceanSpeed", ref _oceanSpeed, 0f, 0.1f);

            ImGui.End();
        }
    }
}