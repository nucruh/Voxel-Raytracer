
public class PerlinNoise
{
    private int[] permutation;

    double amplitude = 1;
    double frequency = 1;

    double lacunarity = 2.1042;
    double persistance = 0.45;

    int octaves = 2;

    double precompMaxAmp = 0;

    public PerlinNoise(int seed = 0)
    {
        Random rand = new Random(seed);
        permutation = new int[512];

        double amp = amplitude;

        for (int i = 0; i < octaves; i++)
        {
            precompMaxAmp += amp;
            amp *= persistance;
        }

        int[] p = new int[256];
        for (int i = 0; i < 256; i++) p[i] = i;

        // Shuffle
        for (int i = 255; i > 0; i--)
        {
            int j = rand.Next(i + 1);
            int tmp = p[i];
            p[i] = p[j];
            p[j] = tmp;
        }

        for (int i = 0; i < 512; i++) permutation[i] = p[i & 255];
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static double Lerp(double a, double b, double t) => a + t * (b - a);

    private static double Grad(int hash, double x, double y, double z)
    {
        int h = hash & 15;
        double u = h < 8 ? x : y;
        double v = h < 4 ? y : h == 12 || h == 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }


    public double Noise(double x, double y, double z)
    {
        int X = (int)Math.Floor(x) & 255;
        int Y = (int)Math.Floor(y) & 255;
        int Z = (int)Math.Floor(z) & 255;

        x -= Math.Floor(x);
        y -= Math.Floor(y);
        z -= Math.Floor(z);

        double u = Fade(x);
        double v = Fade(y);
        double w = Fade(z);

        int aaa = permutation[permutation[permutation[X] + Y] + Z];
        int aba = permutation[permutation[permutation[X] + Y + 1] + Z];
        int aab = permutation[permutation[permutation[X] + Y] + Z + 1];
        int abb = permutation[permutation[permutation[X] + Y + 1] + Z + 1];
        int baa = permutation[permutation[permutation[X + 1] + Y] + Z];
        int bba = permutation[permutation[permutation[X + 1] + Y + 1] + Z];
        int bab = permutation[permutation[permutation[X + 1] + Y] + Z + 1];
        int bbb = permutation[permutation[permutation[X + 1] + Y + 1] + Z + 1];

        double res = Lerp(
            Lerp(
                Lerp(Grad(aaa, x, y, z), Grad(baa, x - 1, y, z), u),
                Lerp(Grad(aba, x, y - 1, z), Grad(bba, x - 1, y - 1, z), u),
                v
            ),
            Lerp(
                Lerp(Grad(aab, x, y, z - 1), Grad(bab, x - 1, y, z - 1), u),
                Lerp(Grad(abb, x, y - 1, z - 1), Grad(bbb, x - 1, y - 1, z - 1), u),
                v
            ),
            w
        );

        return (res + 1.0) / 2.0;
    }


    public double FBM3D(double x, double y, double z, double heightModifier)
    {
        double result = 0;

        double amplitude = this.amplitude;
        double frequency = this.frequency;

        double sharpness = 15;
        double reverseSharp = 15;

        double terrainNoiseFreq = 0.005;
        double terrainNoise = Noise(x * terrainNoiseFreq, y * terrainNoiseFreq, z * terrainNoiseFreq);
        double blendFactor = terrainNoise;

        int octaveCount = 4;

        double erosionNoiseFreq = 0.175;
        double erosion = Noise(x * erosionNoiseFreq, y * erosionNoiseFreq, z * erosionNoiseFreq);
        erosion = Math.Clamp(erosion, 0, 1);

        double[,] splinePoints = new double[,] { { 0.0, 0 }, { 0.25, 7 }, { 0.3, 18 }, { 0.6, 30 }, { 0.8, 100 }, {1.0, 160} };
        double heightMod = splinePoints[splinePoints.GetLength(0) - 1, 1];

        for (int i = 0; i < splinePoints.GetLength(0) - 1; i++)
        {
            double x0 = splinePoints[i, 0];
            double y0 = splinePoints[i, 1];
            double x1 = splinePoints[i + 1, 0];
            double y1 = splinePoints[i + 1, 1];

            if (erosion >= x0 && erosion <= x1)
            {
                double t = (erosion - x0) / (x1 - x0);

                t = t * t * (3 - 2 * t);

                heightMod = y0 + t * (y1 - y0);
                break;
            }
        }

        heightModifier = heightMod;

        sharpness *= heightMod / 200.0;
        reverseSharp /= (heightMod * 200.0);


        for (int i = 0; i < octaveCount; i++)
        {
            double noiseValue = Noise(x * frequency, y * frequency, z * frequency);

            double plain = noiseValue;
            double hill = noiseValue * noiseValue;
            double blended = plain * (1 - blendFactor) + hill * blendFactor;

            double ridge = 1.0 - Math.Abs(blended - 0.5) * 2.0;
            double ridgeValue = Math.Pow(ridge, sharpness);

            result += ridgeValue * amplitude;

            amplitude *= persistance;
            frequency *= lacunarity;
        }

        result /= octaveCount;
        result += heightMod / 20;
        result = Math.Clamp(result, 0, 255);

        return result;
    }




}
