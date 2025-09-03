using Raylib_cs;

namespace RaylibErosionStandalone;



internal  class ClipShaders
{
    static int clipShadersCount = 0;
    private const int CLIP_SHADERS_COUNT = 1; // number of shaders that use a clipPlane

    Shader clipShaders[CLIP_SHADERS_COUNT];
    public int clipShaderHeightLocs[CLIP_SHADERS_COUNT];
    public int clipShaderTypeLocs[CLIP_SHADERS_COUNT];

    public int AddClipShader(Shader shader)
    {
       
        clipShaders[clipShadersCount] = shader;
        clipShaderHeightLocs[clipShadersCount] = Raylib.GetShaderLocation(shader, "cullHeight");
        clipShaderTypeLocs[clipShadersCount] = Raylib.GetShaderLocation(shader, "cullType");
        clipShadersCount++;
        return clipShadersCount - 1;
    }
}