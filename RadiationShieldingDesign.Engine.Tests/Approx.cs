namespace RadiationShieldingDesign.Engine.Tests;

internal static class Approx
{
    public const double RelTol = 1e-6;

    public static void Equal(double expected, double actual, double relTol = RelTol)
    {
        double scale = Math.Max(Math.Abs(expected), 1e-300);
        double diff = Math.Abs(expected - actual);
        Assert.True(diff <= relTol * scale,
            $"Expected {expected:R}, actual {actual:R}, rel diff {diff / scale:E3}");
    }
}
