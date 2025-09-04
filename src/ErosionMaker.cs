using System.Numerics;
using System.Xml;

namespace RaylibErosionStandalone;

internal sealed class ErosionMaker
{
    // used to sample a point in the heightmap and get the gradient
    class HeightAndGradient
    {
        public float height;
        public float gradientX;
        public float gradientY;
    }


    // describes the shape of the smoothing to apply to map borders
    public enum GradientType
    {
        SQUARE = 0,
        CIRCLE = 1,
        DIAMOND = 2,
        STAR = 3,
    }

    // indices and weights of erosion brush precomputed for every cell
    // it's a cache used to speed up the area of effect erosion of a droplet
    List<List<int>>? erosionBrushIndices; // for each cell, a reference to neighbors is held
    List<List<float>> erosionBrushWeights; // for each cell, a reference to how much it influences neighbors

    Random rand;
    int currentSeed; // current random seed
    int currentErosionRadius;
    int currentMapSize;

    int erosionRadius = 6; //12; // Range (2, 8)

    float inertia = 0.05f; // range (0, 1) at zero, water will instantly change direction to flow downhill. At 1, water will never change direction. 

    float sedimentCapacityFactor = 6.0f; // multiplier for how much sediment a droplet can carry
    float minSedimentCapacity = 0.01f; // used to prevent carry capacity getting too close to zero on flatter terrain
    float erodeSpeed = 0.3f; // range (0, 1) how easily a droplet removes sediment
    float depositSpeed = 0.3f; // range (0, 1) how easily a droplet deposits sediment
    float evaporateSpeed = 0.01f; // range (0, 1) droplets evaporate during their lifetime, reducing mass
    float gravity = 4.0f; // determines speed increase of the droplet upon a slope
    int maxDropletLifetime = 60;

    float initialWaterVolume = 1;
    float initialSpeed = 1;

    private ErosionMaker()
    {
    }

    private static ErosionMaker _instance;

    public static ErosionMaker GetInstance()
    {
        if (_instance == null)
        {
            _instance = new ErosionMaker();
        }

        return _instance;
    }

    void Initialize(int mapSize, bool resetSeed)
    {
        // initialization randomizes the generator and precomputes indices and weights of erosion brush

        if (resetSeed)
        {
            var newseed = DateTime.Now.Millisecond;
            rand = new Random(newseed);
            currentSeed = newseed;
        }

        if (erosionBrushIndices == null || currentErosionRadius != erosionRadius || currentMapSize != mapSize)
        {
            InitializeBrushIndices(mapSize, erosionRadius);
            currentErosionRadius = erosionRadius;
            currentMapSize = mapSize;
        }
    }

    // simulate erosion with the given amount of droplets
    public void Erode(ref float[] mapData, int mapSize, int dropletAmount, bool resetSeed)
    {
        Initialize(mapSize, resetSeed);

        for (var iteration = 0; iteration < dropletAmount; iteration++)
        {
            // create water droplet at random point on map (not bound to cell)

            //TODO check if equivalence is correct. Indeed in the original code, it should be  (rand() % mapSize) + 0
            float posX = (rand.Next(mapSize)) + 0;
            float posY = (rand.Next(mapSize)) + 0;
            //float posX = (rand() % (mapSize - 1)) + 0;
            //float posY = (rand() % (mapSize - 1)) + 0;
            float dirX = 0;
            float dirY = 0;
            double speed = initialSpeed;
            var water = initialWaterVolume;
            float sediment = 0; // sediment currently carried

            for (var lifetime = 0; lifetime < maxDropletLifetime; lifetime++)
            {
                // droplet position bound to cell
                var nodeX = (int)posX;
                var nodeY = (int)posY;
                var dropletIndex = nodeY * mapSize + nodeX;
                // calculate droplet's offset inside the cell (0,0) = at NW node, (1,1) = at SE node
                var cellOffsetX = posX - (float)nodeX;
                var cellOffsetY = posY - (float)nodeY;

                // calculate droplet's height and direction of flow with bilinear interpolation of surrounding heights
                var heightAndGradient = CalculateHeightAndGradient(ref mapData, mapSize, posX, posY);

                // update the droplet's direction and position (move position 1 unit regardless of speed)
                dirX = (dirX * inertia -
                        heightAndGradient.gradientX * (1 - inertia)); // lerp with old dir by using inertia as mix value
                dirY = (dirY * inertia - heightAndGradient.gradientY * (1 - inertia));

                // normalize direction
                var len = (float)Math.Sqrt(dirX * dirX + dirY * dirY);
                if (len > 0.0001f)
                {
                    dirX /= len;
                    dirY /= len;
                }

                // update droplet position based on direction (move 1 unit)
                posX += dirX;
                posY += dirY;

                // stop simulating droplet if it's not moving or has flowed over edge of map
                if ((dirX == 0 && dirY == 0) || posX < 0 || posX >= mapSize - 1 || posY < 0 || posY >= mapSize - 1)
                {
                    break;
                }

                // find the droplet's new height and calculate the deltaHeight
                var newHeight = CalculateHeightAndGradient(ref mapData, mapSize, posX, posY).height;
                var deltaHeight = newHeight - heightAndGradient.height;

                // calculate the droplet's sediment capacity (higher when moving fast down a slope and contains lots of water)
                var sedimentCapacity = Math.Max(-deltaHeight * speed * water * sedimentCapacityFactor,
                    minSedimentCapacity);

                // if carrying more sediment than capacity, or if flowing uphill:
                if (sediment > sedimentCapacity || deltaHeight > 0)
                {
                    // DEPOSIT

                    // if moving uphill (deltaHeight > 0) try fill up to the current height, otherwise deposit a fraction of the excess sediment
                    var amountToDeposit = (deltaHeight > 0)
                        ? Math.Min(deltaHeight, sediment)
                        : (sediment - sedimentCapacity) * depositSpeed;
                    sediment -= (float)amountToDeposit;

                    // add the sediment to the four nodes of the current cell using bilinear interpolation
                    // deposition is not distributed over a radius (like erosion) so that it can fill small pits
                    mapData[dropletIndex + mapSize + 1] += (float)(amountToDeposit * cellOffsetX * cellOffsetY);
                }
                else
                {
                    // ERODE

                    // erode a fraction of the droplet's current carry capacity.
                    // clamp the erosion to the change in height so that it doesn't dig a hole in the terrain behind the droplet
                    var amountToErode = Math.Min((sedimentCapacity - sediment) * erodeSpeed, -deltaHeight);

                    // use erosion brush to erode from all nodes inside the droplet's erosion radius
                    for (var brushPointIndex = 0;
                         brushPointIndex < erosionBrushIndices[dropletIndex].Count();
                         brushPointIndex++)
                    {
                        int nodeIndex = (erosionBrushIndices[dropletIndex])[brushPointIndex];
                        var weighedErodeAmount =
                            amountToErode * (erosionBrushWeights[dropletIndex])[brushPointIndex];
                        var deltaSediment = (mapData[nodeIndex] < weighedErodeAmount)
                            ? mapData[nodeIndex]
                            : weighedErodeAmount;
                        mapData[nodeIndex] -= (float)deltaSediment;
                        sediment += (float)deltaSediment;
                    }
                }

                // update droplet's speed and water content
                speed = (float)Math.Sqrt(speed * speed + deltaHeight * gravity);
                if (double.IsNaN(speed))
                    speed = 0; // fix per alcuni NaN dovuti a speed * speed + deltaHeight * gravity negativo
                water *= (1 - evaporateSpeed); // evaporate water
            }
        }
    }

    // applies a radial gradient to the heightmap in order to flatten the outer borders
    public void Gradient(ref float[] mapData, int mapSize, float normalizedOffset, GradientType gradientType)
    {
        float radius = ((float)mapSize / 2.0f);
        for (var y = 0; y < mapSize; y++)
        {
            for (var x = 0; x < mapSize; x++)
            {
                int index = y * mapSize + x;
                float gradient = 0.0f;
                switch (gradientType)
                {
                    case GradientType.SQUARE:
                        gradient = Math.Max(Math.Abs((float)x - radius), Math.Abs((float)y - radius)) /
                                   (radius); // Chebyshev distance
                        break;

                    case GradientType.CIRCLE:
                        gradient = Math.Min(
                            ((x - radius) * (x - radius) + (y - radius) * (y - radius)) / (radius * radius),
                            1.0f); // Euclidean distance
                        break;

                    case GradientType.DIAMOND:
                        gradient = Math.Min((Math.Abs((float)x - radius) + Math.Abs((float)y - radius)) / (radius),
                            1.0f); // Manhattan distance
                        break;

                    case GradientType.STAR:
                    {
                        float g1 = Math.Min((Math.Abs((float)x - radius) + Math.Abs((float)y - radius)) / (radius),
                            1.0f); // Manhattan distance
                        float g2 = Math.Max(Math.Abs((float)x - radius), Math.Abs((float)y - radius)) /
                                   (radius); // Chebyshev distance
                        gradient = Tools.Lerp(g1, g2, 0.7f); // mix manhattan and chebyshev by desired value
                    }
                        break;

                    default:
                        gradient = Math.Min(
                            ((x - radius) * (x - radius) + (y - radius) * (y - radius)) / (radius * radius),
                            1.0f); // Euclidean distance
                        break;
                }

                gradient = 1 - gradient; // invert
                mapData[index] *= gradient; // multiply height by given linear gradient
            }
        }
    }

    HeightAndGradient CalculateHeightAndGradient(ref float[] mapData, int mapSize, float posX, float posY)
    {
        int coordX = (int)posX;
        int coordY = (int)posY;

        // calculate droplet's offset inside the cell (0,0) = at NW node, (1,1) = at SE node
        float x = posX - (float)coordX;
        float y = posY - (float)coordY;

        // calculate heights of the four nodes of the droplet's cell
        int nodeIndexNW = coordY * mapSize + coordX;

        float heightNW = mapData[nodeIndexNW];
        float heightNE = mapData[nodeIndexNW + 1];
        float heightSW = mapData[nodeIndexNW + mapSize];
        float heightSE = mapData[nodeIndexNW + mapSize + 1];

        // calculate droplet's direction of flow with bilinear interpolation of height difference along the edges
        float gradientX = (heightNE - heightNW) * (1 - y) + (heightSE - heightSW) * y;
        float gradientY = (heightSW - heightNW) * (1 - x) + (heightSE - heightNE) * x;

        // calculate height with bilinear interpolation of the heights of the nodes of the cell
        float height = heightNW * (1 - x) * (1 - y) + heightNE * x * (1 - y) + heightSW * (1 - x) * y +
                       heightSE * x * y;

        HeightAndGradient ret = new();
        ret.height = height;
        ret.gradientX = gradientX;
        ret.gradientY = gradientY;
        return ret;
    }

    void InitializeBrushIndices(int mapSize, int radius)
    {
        erosionBrushIndices = new List<List<int>>(mapSize * mapSize); // each cell stores its neighbors' indices
        erosionBrushWeights = new List<List<float>>(mapSize * mapSize); // each cell stores its neighbors' weight

        //TODO check that initial values are 0 as expected
        var xOffsets = new List<int>(radius * radius * 4);
        var yOffsets = new List<int>(radius * radius * 4);
        var weights = new List<float>(radius * radius * 4);

        //List<int> xOffsets((size_t) radius *radius * 4, 0);
        //List<int> yOffsets((size_t) radius *radius * 4, 0);
        //List<float> weights((size_t) radius *radius * 4, 0);

        float weightSum = 0;
        int addIndex = 0;

        for (int i = 0; i < erosionBrushIndices.Count; i++) // va bene la prima coord
        {
            int centreX = i % mapSize; // x coordinate by cell
            int centreY = i / mapSize; // y coodinate by cell

            if (centreY <= radius || centreY >= mapSize - radius || centreX <= radius ||
                centreX >= mapSize - radius) // loop only not too close to borders
            {
                weightSum = 0;
                addIndex = 0;
                for (int y = -radius; y <= radius; y++) // loop neighbors
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        float sqrDst = x * x + y * y;
                        if (sqrDst < radius * radius) // take only those inside the radius of influence
                        {
                            int coordX = centreX + x;
                            int coordY = centreY + y;

                            if (coordX >= 0 && coordX < mapSize && coordY >= 0 && coordY < mapSize)
                            {
                                // add to brush index
                                float weight = (float)(1 - Math.Sqrt(sqrDst) / radius); // euclidean distance -> circle
                                weightSum += weight;
                                weights[addIndex] = weight;
                                xOffsets[addIndex] = x;
                                yOffsets[addIndex] = y;
                                addIndex++;
                            }
                        }
                    }
                }
            }

            var numEntries = addIndex;
            erosionBrushIndices[i] = new List<int>(numEntries);
            erosionBrushWeights[i] = new List<float>(numEntries);

            for (var j = 0; j < numEntries; j++)
            {
                (erosionBrushIndices[i])[j] = (yOffsets[j] + centreY) * mapSize + xOffsets[j] + centreX;
                (erosionBrushWeights[i])[j] = weights[j] / weightSum;
            }
        }
    }

    float RemapValue(float value)
    {
        const int points = 4;
        // describe the remapping of the grayscale heights (beach and craters)
        List<Vector2> point = new List<Vector2>
        {
            new Vector2(0.0f, 0.0f), // initial point (keep)


            new Vector2(0.15f, 0.16f), // flatten beach
            new Vector2(0.2f, 0.16f),
            /*{0.3f,		0.4f}, // add some craters
            {0.4f,		0.3f},*/

            new Vector2(1.0f, 1.0f) // final point (keep)
        };

        if (value < 0.0f)
            return value;
        for (var i = 1; i < points; i++)
        {
            if (value < point[i].X)
                return Tools.Lerp(point[i - 1].Y, point[i].Y,
                    (value - point[i - 1].X) / (point[i].X - point[i - 1].X)); // lerp based on the interval you're on
        }

        return value;
    }

    public Vector3 GetNormal(ref float[] mapData, int mapSize, int x, int y)
    {
        // value from trial & error.
        // seems to work fine for the scales we are dealing with.
        // almost equivalent code in terrain shader to get normal
        var strength = 20.0f;

        int u, v;

        u = Math.Min(Math.Max(x - 1, 0), mapSize - 1);
        v = Math.Min(Math.Max(y + 1, 0), mapSize - 1);
        float bl = mapData[v * mapSize + u];

        u = Math.Min(Math.Max(x, 0), mapSize - 1);
        v = Math.Min(Math.Max(y + 1, 0), mapSize - 1);
        float b = mapData[v * mapSize + u];

        u = Math.Min(Math.Max(x + 1, 0), mapSize - 1);
        v = Math.Min(Math.Max(y + 1, 0), mapSize - 1);
        float br = mapData[v * mapSize + u];

        u = Math.Min(Math.Max(x - 1, 0), mapSize - 1);
        v = Math.Min(Math.Max(y, 0), mapSize - 1);
        float l = mapData[v * mapSize + u];

        u = Math.Min(Math.Max(x + 1, 0), mapSize - 1);
        v = Math.Min(Math.Max(y, 0), mapSize);
        float r = mapData[v * mapSize + u];

        u = Math.Min(Math.Max(x - 1, 0), mapSize - 1);
        v = Math.Min(Math.Max(y - 1, 0), mapSize - 1);
        float tl = mapData[v * mapSize + u];

        u = Math.Min(Math.Max(x, 0), mapSize - 1);
        v = Math.Min(Math.Max(y - 1, 0), mapSize - 1);
        float t = mapData[v * mapSize + u];

        u = Math.Min(Math.Max(x + 1, 0), mapSize - 1);
        v = Math.Min(Math.Max(y - 1, 0), mapSize - 1);
        float tr = mapData[v * mapSize + u];

        // compute dx using Sobel:
        //           -1 0 1 
        //           -2 0 2
        //           -1 0 1
        float dX = tr + 2.0f * r + br - tl - 2.0f * l - bl;

        // compute dy using Sobel:
        //           -1 -2 -1 
        //            0  0  0
        //            1  2  1
        float dY = bl + 2.0f * b + br - tl - 2.0f * t - tr;

        return Vector3.Normalize(new Vector3(-dX, 1.0f / strength, -dY));
    }

    public void Remap(ref float[] map, int mapSize)
    {
        for (var i = 0; i < mapSize * mapSize; i++)
        {
            map[i] = RemapValue(map[i]);
        }
    }
}