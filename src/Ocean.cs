using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace RaylibErosionStandalone;

class Ocean
{

    // ocean shader
    public float _oceanSpeed = 0.03f;
    public int _oceanMoveFactorLoc = -1;
    public float _oceanMoveFactor;

    // wave Shader
    private int secondsLoc = -1;
    private float seconds = 0.0f;

    public Model Model;

    public Model PrepareOcean(Texture2D reflectionTexture,
     Texture2D refractionTexture)
    {
        Raylib.TraceLog(TraceLogLevel.Info, "PrepareOcean...");

        var oceanMesh = Raylib.GenMeshPlane(5120, 5120, 10, 10);
        Model = Raylib.LoadModelFromMesh(oceanMesh);

        //SetWaveShader();
        SetWave2Shader(reflectionTexture, refractionTexture);

        //SetOceanShader(reflectionTexture, refractionTexture);

        Raylib.TraceLog(TraceLogLevel.Info, "PrepareOcean OK");

        return Model;
    }

    private void SetOceanShader(Texture2D reflectionTexture,
        Texture2D refractionTexture)
    {
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
        // ocean shader
        var shader = Raylib.GetMaterial(ref Model, 0).Shader;
        Raylib.SetShaderValue(shader, _oceanMoveFactorLoc, _oceanMoveFactor, ShaderUniformDataType.Float);


        // wave shader
        seconds += Raylib.GetFrameTime();
        Raylib.SetShaderValue(shader, secondsLoc, seconds, ShaderUniformDataType.Float);
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

    private void SetWaveShader()
    {
        // Load texture texture to apply shaders
        var texture = Raylib.LoadTexture("resources/space.png");
        Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);

        // Load shader and setup location points and values
        var shader = Raylib.LoadShader("resources/shaders/glsl330/wave.vert", "resources/shaders/glsl330/wave.frag");

        Raylib.SetMaterialTexture(ref Model, 0, MaterialMapIndex.Albedo, ref texture);

        secondsLoc = Raylib.GetShaderLocation(shader, "seconds");
        var freqXLoc = Raylib.GetShaderLocation(shader, "freqX");
        var freqYLoc = Raylib.GetShaderLocation(shader, "freqY");
        var ampXLoc = Raylib.GetShaderLocation(shader, "ampX");
        var ampYLoc = Raylib.GetShaderLocation(shader, "ampY");
        var speedXLoc = Raylib.GetShaderLocation(shader, "speedX");
        var speedYLoc = Raylib.GetShaderLocation(shader, "speedY");

        // Shader uniform values that can be updated at any time
        var freqX = 25.0f;
        var freqY = 25.0f;
        var ampX = 5.0f;
        var ampY = 5.0f;
        var speedX = 8.0f;
        var speedY = 8.0f;

        var screenSize = new Vector2((float)Raylib.GetScreenWidth(), (float)Raylib.GetScreenHeight());
        Raylib.SetShaderValue(shader, Raylib.GetShaderLocation(shader, "size"),  screenSize, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(shader, freqXLoc, freqX, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, freqYLoc, freqY, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, ampXLoc,  ampX, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, ampYLoc,  ampY, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, speedXLoc,  speedX, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, speedYLoc,  speedY, ShaderUniformDataType.Float);

        Raylib.SetMaterialShader(ref Model, 0, ref shader);
    }

    private void SetWave2Shader(Texture2D reflectionTexture,
        Texture2D refractionTexture)
    {
        // Load texture texture to apply shaders
        var texture = Raylib.LoadTexture("resources/space.png");
        Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);

        // Load shader and setup location points and values
        var shader = Raylib.LoadShader("resources/shaders/glsl330/wave2.vert", "resources/shaders/glsl330/wave2.frag");

        //Raylib.SetMaterialTexture(ref Model, 0, MaterialMapIndex.Albedo, ref texture);

        Raylib.SetMaterialTexture(ref Model, 0, MaterialMapIndex.Albedo, ref reflectionTexture);
        Raylib.SetMaterialTexture(ref Model, 0, MaterialMapIndex.Metalness, ref refractionTexture);

        var _duDvMap = Raylib.LoadTexture("resources/waterDUDV.png");
        Raylib.SetTextureFilter(_duDvMap, TextureFilter.Bilinear);
        Raylib.GenTextureMipmaps(ref _duDvMap);
        Raylib.SetMaterialTexture(ref Model, 0, MaterialMapIndex.Normal, ref _duDvMap);

        secondsLoc = Raylib.GetShaderLocation(shader, "seconds");
        var freqXLoc = Raylib.GetShaderLocation(shader, "freqX");
        var freqYLoc = Raylib.GetShaderLocation(shader, "freqY");
        var ampXLoc = Raylib.GetShaderLocation(shader, "ampX");
        var ampYLoc = Raylib.GetShaderLocation(shader, "ampY");
        var speedXLoc = Raylib.GetShaderLocation(shader, "speedX");
        var speedYLoc = Raylib.GetShaderLocation(shader, "speedY");

        unsafe
        {
            shader.Locs[(int)ShaderLocationIndex.MatrixModel] =
                Raylib.GetShaderLocation(shader, "matModel");

            int locIndex = Raylib.GetShaderLocation(shader, "viewPos");
            if (locIndex < 0)
                Raylib.TraceLog(TraceLogLevel.Error, "VectorView loc index < 0");

            shader.Locs[(int)ShaderLocationIndex.VectorView] = locIndex;
        }

        // Shader uniform values that can be updated at any time
        var freqX = 25.0f;
        var freqY = 25.0f;
        var ampX = 5.0f;
        var ampY = 5.0f;
        var speedX = 8.0f;
        var speedY = 8.0f;

        var screenSize = new Vector2((float)Raylib.GetScreenWidth(), (float)Raylib.GetScreenHeight());
        Raylib.SetShaderValue(shader, Raylib.GetShaderLocation(shader, "size"), screenSize, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(shader, freqXLoc, freqX, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, freqYLoc, freqY, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, ampXLoc, ampX, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, ampYLoc, ampY, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, speedXLoc, speedX, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, speedYLoc, speedY, ShaderUniformDataType.Float);

        Raylib.SetMaterialShader(ref Model, 0, ref shader);
    }
}