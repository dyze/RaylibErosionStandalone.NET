using Raylib_cs;

namespace RaylibErosionStandalone;

class OceanFloor
{
    private Model Model;

    public Model PrepareOceanFloor(Terrain terrain)
    {
        unsafe
        {
            var whiteImage = Raylib.GenImageColor(8, 8, Color.Black);
            var whiteTexture = Raylib.LoadTextureFromImage(whiteImage);
            Raylib.UnloadImage(whiteImage);
            var oceanFloorMesh = Raylib.GenMeshPlane(5120, 5120, 10, 10);
            Model = Raylib.LoadModelFromMesh(oceanFloorMesh);

            Model.Transform = Raymath.MatrixTranslate(0, -1.2f, 0);

            Model.Materials[0].Maps[0].Texture = terrain.TerrainGradient;
            Model.Materials[0].Maps[2].Texture = whiteTexture;


            Model.Materials[0].Shader = terrain.Model.Materials[0].Shader;
        }

        return Model;
    }
}