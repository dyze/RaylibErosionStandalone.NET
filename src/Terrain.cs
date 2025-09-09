using System.Numerics;
using Raylib_cs;

namespace RaylibErosionStandalone;

class Terrain
{
    public readonly ErosionMaker ErosionMaker = ErosionMaker.GetInstance();


    public const int MapResolution = 512; // width and height of heightmap


    public Model Model;

    public Texture2D TerrainGradient;
    public int TerrainDaytimeLoc = -1;
    public int TerrainAmbientLoc = -1;

    public float[] MapData;
    private Texture2D _heightmapTexture;

    private unsafe Color* _pixels;



    public Model PrepareTerrain(ClipShaders clipShaders,
        Vector4 ambientColorAndIntensity)
    {
        var terrainMesh = Raylib.GenMeshPlane(32, 32, 256, 256); // Generate terrain mesh (RAM and VRAM)
        TerrainGradient = Raylib.LoadTexture("resources/terrainGradient.png"); // color ramp of terrain (rock and grass)
        //SetTextureFilter(terrainGradient, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(TerrainGradient, TextureWrap.Clamp);
        Raylib.GenTextureMipmaps(ref TerrainGradient);

        Model = Raylib.LoadModelFromMesh(terrainMesh); // Load model from generated mesh
        Model.Transform = Raymath.MatrixTranslate(0, -1.2f, 0);

        Raylib.SetMaterialTexture(ref Model, 0, MaterialMapIndex.Albedo, ref TerrainGradient);
        Raylib.SetMaterialTexture(ref Model, 0, MaterialMapIndex.Normal, ref _heightmapTexture);

        var shader = Raylib.LoadShader("resources/shaders/terrain.vert", "resources/shaders/terrain.frag");
        Raylib.SetMaterialShader(ref Model, 0, ref shader);

        // Get some shader locations
        unsafe
        {
            Model.Materials[0].Shader.Locs[(int)ShaderLocationIndex.MatrixModel] =
                Raylib.GetShaderLocation(Model.Materials[0].Shader, "matModel");
            Model.Materials[0].Shader.Locs[(int)ShaderLocationIndex.VectorView] =
                Raylib.GetShaderLocation(Model.Materials[0].Shader, "viewPos");
            TerrainDaytimeLoc = Raylib.GetShaderLocation(shader, "daytime");
        }

        var cs = clipShaders.AddClipShader(shader); // register as clip shader for automatization of clipPlanes
        Raylib.SetShaderValue(shader, clipShaders.clipShaderHeightLocs[cs],
            0.0f,
            ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, clipShaders.clipShaderTypeLocs[cs], 2,
            ShaderUniformDataType.Int);

        // ambient light level
        TerrainAmbientLoc = Raylib.GetShaderLocation(shader, "ambient");
        Raylib.SetShaderValue(shader, TerrainAmbientLoc, ambientColorAndIntensity, ShaderUniformDataType.Vec4);

        var rockNormalMap = Raylib.LoadTexture("resources/rockNormalMap.png"); // normal map
        Raylib.SetTextureFilter(rockNormalMap, TextureFilter.Bilinear);
        Raylib.GenTextureMipmaps(ref rockNormalMap);

        unsafe
        {
            shader.Locs[(int)ShaderLocationIndex.MapRoughness] =
                Raylib.GetShaderLocation(shader, "rockNormalMap");
        }

        Raylib.SetMaterialTexture(ref Model, 0, MaterialMapIndex.Roughness, ref rockNormalMap);

        

        return Model;
    }

    public void PrepareInitialHeightMap()
    {
        unsafe
        {
            var initialHeightmapImage =
                Raylib.GenImagePerlinNoise(MapResolution, MapResolution, 50, 50, 4.0f); // generate fractal perlin noise
            MapData = new float[MapResolution * MapResolution];

            // Extract pixels and put them in mapData
            _pixels = Raylib.LoadImageColors(initialHeightmapImage);
            for (var i = 0; i < MapResolution * MapResolution; i++)
                MapData[i] = _pixels[i].R / 255.0f;
        }

        // Erode
        ErosionMaker.Gradient(ref MapData, MapResolution, 0.5f,
            ErosionMaker.GradientType
                .SQUARE); // apply a centered gradient to smooth out border pixel (create island at center)
        ErosionMaker.Remap(ref MapData, MapResolution); // flatten beaches
        ErosionMaker.Erode(ref MapData, MapResolution, 0, true); // Erode (0 droplets for initialization)

        // Update pixels from mapData to texture
        UpdatePixels();
    }

    public void Erode(int speed)
    {
        ErosionMaker.Erode(ref MapData, MapResolution, speed, false);
        UpdatePixels();

    }

    private unsafe void UpdatePixels()
    {
        // Update pixels
        for (var i = 0; i < MapResolution * MapResolution; i++)
        {
            var val = (byte)(MapData[i] * 255);
            _pixels[i].R = val;
            _pixels[i].G = val;
            _pixels[i].B = val;
            _pixels[i].A = 255;
        }

        Raylib.UnloadTexture(_heightmapTexture);

        //TODO check if replacement for former "Image heightmapImage = Raylib.LoadImageEx(pixels, MapResolution, MapResolution);"
        //Image heightmapImage = new Image();
        var heightmapImage = Raylib.GenImageColor(MapResolution, MapResolution, Color.Red);

        var k = 0;
        var dataAsChars = (byte*)heightmapImage.Data;

        for (var i = 0; i < heightmapImage.Width * heightmapImage.Height * 4; i += 4)
        {
            dataAsChars[i] = _pixels[k].R;
            dataAsChars[i + 1] = _pixels[k].G;
            dataAsChars[i + 2] = _pixels[k].B;
            dataAsChars[i + 3] = _pixels[k].A;
            k++;
        }

        //END_TODO

        _heightmapTexture = Raylib.LoadTextureFromImage(heightmapImage); // Convert image to texture (VRAM)
        Raylib.SetTextureFilter(_heightmapTexture, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(_heightmapTexture, TextureWrap.Clamp);
        Model.Materials[0].Maps[2].Texture = _heightmapTexture;
        Raylib.UnloadImage(heightmapImage); // Unload heightmap image from RAM, already uploaded to VRAM
    }
}