using Xunit;

namespace JinxyClicker;

public class GradeMatrixTests
{
    private const double Tol = 0.001;

    [Fact]
    public void IdentityLeavesAColourUnchanged()
    {
        var (r, g, b) = GradeMatrix.ApplyToColor(GradeMatrix.Identity(), 0.2, 0.6, 0.9);
        Assert.Equal(0.2, r, 3);
        Assert.Equal(0.6, g, 3);
        Assert.Equal(0.9, b, 3);
    }

    [Fact]
    public void FullSaturationIsIdentity()
    {
        var (r, g, b) = GradeMatrix.ApplyToColor(GradeMatrix.Saturation(1.0), 0.8, 0.1, 0.3);
        Assert.Equal(0.8, r, 3);
        Assert.Equal(0.1, g, 3);
        Assert.Equal(0.3, b, 3);
    }

    [Fact]
    public void ZeroSaturationProducesGrey()
    {
        var (r, g, b) = GradeMatrix.ApplyToColor(GradeMatrix.Saturation(0.0), 0.9, 0.2, 0.4);
        Assert.True(System.Math.Abs(r - g) < Tol && System.Math.Abs(g - b) < Tol, "channels should collapse to one grey");
        // Rec.709 luma of the input.
        double luma = 0.2126 * 0.9 + 0.7152 * 0.2 + 0.0722 * 0.4;
        Assert.Equal(luma, r, 3);
    }

    [Fact]
    public void MultiplyingByIdentityKeepsTheMatrix()
    {
        float[] sat = GradeMatrix.Saturation(1.4);
        float[] viaId = GradeMatrix.Multiply(sat, GradeMatrix.Identity());
        for (int i = 0; i < 25; i++) Assert.Equal(sat[i], viaId[i], 4);
    }

    [Fact]
    public void LerpHitsBothEnds()
    {
        float[] a = GradeMatrix.Identity();
        float[] b = GradeMatrix.Saturation(0.0);
        for (int i = 0; i < 25; i++)
        {
            Assert.Equal(a[i], GradeMatrix.Lerp(a, b, 0.0)[i], 4);
            Assert.Equal(b[i], GradeMatrix.Lerp(a, b, 1.0)[i], 4);
        }
    }

    [Fact]
    public void InkDrainsColourToGrey()
    {
        var (r, g, b) = GradeMatrix.ApplyToColor(GradeLooks.For("ink"), 0.9, 0.2, 0.3);
        Assert.True(System.Math.Abs(r - g) < 0.02 && System.Math.Abs(g - b) < 0.02);
    }

    [Fact]
    public void UnknownLookIsIdentity()
    {
        float[] id = GradeMatrix.Identity();
        foreach (string? key in new[] { null, "", "nope", "off" })
            for (int i = 0; i < 25; i++)
                Assert.Equal(id[i], GradeLooks.For(key)[i], 4);
    }

    [Fact]
    public void OutputIsClampedToByteRange()
    {
        var (r, g, b) = GradeMatrix.ApplyToColor(GradeMatrix.Brightness(0.6), 0.9, 0.9, 0.9);
        Assert.Equal(1.0, r, 3);
        Assert.Equal(1.0, g, 3);
        Assert.Equal(1.0, b, 3);

        var (r2, _, _) = GradeMatrix.ApplyToColor(GradeMatrix.Brightness(-0.6), 0.1, 0.1, 0.1);
        Assert.Equal(0.0, r2, 3);
    }

    [Fact]
    public void LerpToStrengthZeroIsIdentity()
    {
        float[] id = GradeMatrix.Identity();
        for (int i = 0; i < 25; i++)
            Assert.Equal(id[i], GradeLooks.Lerp("punch", 0.0)[i], 4);
    }
}
