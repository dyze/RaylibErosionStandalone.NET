using Raylib_cs;

namespace RaylibErosionStandalone;



internal  class ClipShaders
{
    static int clipShadersCount = 0;
    private const int CLIP_SHADERS_COUNT = 1; // number of shaders that use a clipPlane

    public List<Shader> clipShaders = [];
    public List<int> clipShaderHeightLocs;
    public List<int> clipShaderTypeLocs;

    public int AddClipShader(Shader shader)
    {
        clipShaders.Add(shader);
        clipShaderHeightLocs.Add(Raylib.GetShaderLocation(shader, "cullHeight"));
        clipShaderTypeLocs.Add(Raylib.GetShaderLocation(shader, "cullType"));

        clipShadersCount++;
        return clipShadersCount - 1;
    }
}