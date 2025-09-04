using Raylib_cs;
using System.Numerics;

namespace RaylibErosionStandalone;

internal class TreeBillboard
{
    public Texture2D texture;
    public Vector3 position;
    public float scale = 1.0f;
    public Color color = Color.White;

    public TreeBillboard(Texture2D texture, Vector3 position, float scale, Color color)
    {
        this.texture = texture;
        this.position = position;
        this.scale = scale;
        this.color = color;
    }
}