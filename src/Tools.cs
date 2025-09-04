namespace RaylibErosionStandalone;

internal static class Tools
{
    static Random rnd = new Random();

    public static float randomRange(float min, float max)
    {
        //TODO check equivalence. We should use same Random object as for ErosionMaker if we want to stick to original code
        return min + rnd.Next(0, 32767) / ((float)32767 / (max - min));
        //return min + static_cast <float> (rand()) / (static_cast <float> (RAND_MAX / (max - min)));
    }

    public static float Lerp(float firstFloat, float secondFloat, float by)
    {
        return firstFloat * (1 - by) + secondFloat * by;
    }
}