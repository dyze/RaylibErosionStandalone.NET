using Raylib_cs;
using System.Numerics;

namespace RaylibErosionStandalone;

class Trees
{
    private const float TreeSpeed = 0.125f;

    private const int TreeTextureCount = 19; // number of textures for a tree
    private const int TreeCount = 8190; // number of tree billboards
    public Shader _treeShader; // shader used for tree billboards
    public int _treeAmbientLoc = -1;
    public float _treeMoveFactor;
    public int _treeMoveFactorLoc = -1;
    private readonly List<Texture2D> _treeTextures = [];

    public List<TreeBillboard> _trees = new(); // fill with tree data

    private Terrain _terrain;

    public void PrepareTrees(Terrain terrain,
        Vector4 ambientColorAndIntensity)
    {
        _terrain = terrain;

        unsafe
        {
            for (var i = 0; i < TreeTextureCount; i++)
            {
                var texture = Raylib.LoadTexture($"resources/trees/b/{i}.png");
                _treeTextures.Add(texture); // variant b of trees looks much better
                Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
                //GenTextureMipmaps(&treeTextures[i]); // looks better without
            }

            GenerateTrees(terrain.ErosionMaker, ref terrain.MapData, _treeTextures, ref _trees, true);

            var treeMaterial = Raylib.LoadMaterialDefault();
            _treeShader = Raylib.LoadShader("resources/shaders/vegetation.vert", "resources/shaders/vegetation.frag");
            _treeShader.Locs[(int)ShaderLocationIndex.MatrixModel] = Raylib.GetShaderLocation(_treeShader, "matModel");
            _treeAmbientLoc = Raylib.GetShaderLocation(_treeShader, "ambient");
            Raylib.SetShaderValue(_treeShader, _treeAmbientLoc, ambientColorAndIntensity, ShaderUniformDataType.Vec4);
            treeMaterial.Shader = _treeShader;

            var duDvMap = Raylib.LoadTexture("resources/waterDUDV.png");
            Raylib.SetTextureFilter(duDvMap, TextureFilter.Bilinear);
            Raylib.GenTextureMipmaps(ref duDvMap);

            treeMaterial.Maps[1].Texture = duDvMap;
            _treeMoveFactorLoc = Raylib.GetShaderLocation(_treeShader, "moveFactor");
        }
    }

    public void AnimateTrees()
    {
        _treeMoveFactor += TreeSpeed * Raylib.GetFrameTime();
        while (_treeMoveFactor > 1.0f)
            _treeMoveFactor -= 1.0f;
    }

    public void ApplyTreeMoveFactor()
    {
        Raylib.SetShaderValue(_treeShader, _treeMoveFactorLoc, _treeMoveFactor, ShaderUniformDataType.Float);
    }

    // generates (or regenerates) all tree billboards
    private void GenerateTrees(ErosionMaker erosionMaker, ref float[] mapData, List<Texture2D> treeTextures,
        ref List<TreeBillboard> trees, bool generateNew)
    {
        if (generateNew)
            trees.Clear();

        var billPosition = Vector3.Zero;
        Vector3 billNormal;
        var grassSlopeThreshold = 0.2f; // different than in the terrain shader
        var grassBlendAmount = 0.55f;
        float grassWeight;
        var billColor = Color.White;

        for (var i = 0; i < TreeCount; i++) // 8190 max billboards, more than that and they are not cached anymore
        {
            int px, py;
            do
            {
                // try to generate a billboard
                billPosition.X = Tools.randomRange(-16, 16);
                billPosition.Z = Tools.randomRange(-16, 16);
                px = (int)(((billPosition.X + 16.0f) / 32.0f) * (Terrain.MapResolution - 1));
                py = (int)(((billPosition.Z + 16.0f) / 32.0f) * (Terrain.MapResolution - 1));
                billNormal = erosionMaker.GetNormal(ref mapData, Terrain.MapResolution, px, py);
                billPosition.Y = mapData[py * Terrain.MapResolution + px] * 8 - 1.1f;

                var slope = 1.0f - billNormal.Y;
                var grassBlendHeight = grassSlopeThreshold * (1.0f - grassBlendAmount);
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

    public void GenerateTrees()
    {
       GenerateTrees(_terrain.ErosionMaker, ref _terrain.MapData, _treeTextures, ref _trees, false);
    }
}