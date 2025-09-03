namespace RaylibErosionStandalone;

internal static class Tools
{
    static Random rnd = new Random();

    public static float randomRange(float min, float max)
    {
        return min + rnd.NextSingle() / (static_cast<float>(RAND_MAX / (max - min)));
    }
}