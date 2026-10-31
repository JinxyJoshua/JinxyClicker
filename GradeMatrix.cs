using System;

namespace JinxyClicker;

/// <summary>
/// Builds 5×5 colour-grading matrices (the same row-vector convention GDI+ and the
/// Windows Magnification API use). Each matrix is a <c>float[25]</c> in row-major
/// order; a colour <c>[r, g, b, 1, 1]</c> multiplied by it gives the graded colour.
/// Pure maths, so the grading can be tested without a screen.
/// </summary>
public static class GradeMatrix
{
    // Rec.709 luma weights.
    private const double Wr = 0.2126, Wg = 0.7152, Wb = 0.0722;

    public static float[] Identity()
    {
        var m = new float[25];
        m[0] = m[6] = m[12] = m[18] = m[24] = 1f;
        return m;
    }

    /// <summary>s = 1 leaves colour alone, 0 is greyscale, &gt;1 is more saturated.</summary>
    public static float[] Saturation(double s)
    {
        float[] m = Identity();
        double r1 = Wr * (1 - s), g1 = Wg * (1 - s), b1 = Wb * (1 - s);

        m[0] = (float)(r1 + s); m[1] = (float)r1; m[2] = (float)r1;
        m[5] = (float)g1; m[6] = (float)(g1 + s); m[7] = (float)g1;
        m[10] = (float)b1; m[11] = (float)b1; m[12] = (float)(b1 + s);
        return m;
    }

    /// <summary>c = 1 is unchanged; pivots around mid-grey.</summary>
    public static float[] Contrast(double c)
    {
        float[] m = Identity();
        m[0] = m[6] = m[12] = (float)c;
        float o = (float)(0.5 - 0.5 * c);
        m[20] = m[21] = m[22] = o;
        return m;
    }

    /// <summary>Adds a flat lift (or drop) to every channel.</summary>
    public static float[] Brightness(double b)
    {
        float[] m = Identity();
        m[20] = m[21] = m[22] = (float)b;
        return m;
    }

    /// <summary>Per-channel gain — the warm/cool tilt.</summary>
    public static float[] Gain(double gr, double gg, double gb)
    {
        float[] m = Identity();
        m[0] = (float)gr; m[6] = (float)gg; m[12] = (float)gb;
        return m;
    }

    /// <summary>a then b (row-vector order): the colour flows through a, then b.</summary>
    public static float[] Multiply(float[] a, float[] b)
    {
        var r = new float[25];
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
            {
                double sum = 0;
                for (int k = 0; k < 5; k++) sum += a[i * 5 + k] * b[k * 5 + j];
                r[i * 5 + j] = (float)sum;
            }
        return r;
    }

    public static float[] Compose(params float[][] steps)
    {
        float[] m = Identity();
        foreach (float[] s in steps) m = Multiply(m, s);
        return m;
    }

    public static float[] Lerp(float[] a, float[] b, double t)
    {
        var r = new float[25];
        for (int i = 0; i < 25; i++) r[i] = (float)(a[i] * (1 - t) + b[i] * t);
        return r;
    }

    /// <summary>Applies a matrix to one colour (inputs and outputs 0‑1, clamped).</summary>
    public static (double R, double G, double B) ApplyToColor(float[] m, double r, double g, double b)
    {
        // Row vector [r, g, b, 1, 1] × m. The A row (index 3) and the constant row
        // (index 4) both carry a 1.
        double or = r * m[0] + g * m[5] + b * m[10] + m[15] + m[20];
        double og = r * m[1] + g * m[6] + b * m[11] + m[16] + m[21];
        double ob = r * m[2] + g * m[7] + b * m[12] + m[17] + m[22];
        return (Clamp(or), Clamp(og), Clamp(ob));
    }

    private static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}

/// <summary>The grade for each Screen look, matched to the look names on the Mod page.</summary>
public static class GradeLooks
{
    public static float[] For(string? lookId) => lookId switch
    {
        "punch" => GradeMatrix.Compose(GradeMatrix.Saturation(1.75), GradeMatrix.Contrast(1.12), GradeMatrix.Brightness(0.02)),
        "crisp" => GradeMatrix.Compose(GradeMatrix.Saturation(1.18), GradeMatrix.Contrast(1.16), GradeMatrix.Brightness(0.06)),
        "theater" => GradeMatrix.Compose(GradeMatrix.Saturation(0.90), GradeMatrix.Contrast(1.30), GradeMatrix.Gain(1.07, 1.0, 0.92)),
        "amber" => GradeMatrix.Compose(GradeMatrix.Saturation(1.20), GradeMatrix.Gain(1.18, 1.0, 0.80), GradeMatrix.Brightness(0.02)),
        "frost" => GradeMatrix.Compose(GradeMatrix.Saturation(1.10), GradeMatrix.Gain(0.86, 0.97, 1.20), GradeMatrix.Contrast(1.06)),
        "faded" => GradeMatrix.Compose(GradeMatrix.Saturation(0.72), GradeMatrix.Contrast(0.85), GradeMatrix.Gain(1.07, 1.0, 0.90), GradeMatrix.Brightness(0.06)),
        "ink" => GradeMatrix.Compose(GradeMatrix.Saturation(0.0), GradeMatrix.Contrast(1.38)),
        "haze" => GradeMatrix.Compose(GradeMatrix.Saturation(0.82), GradeMatrix.Contrast(0.85), GradeMatrix.Brightness(0.10), GradeMatrix.Gain(1.04, 1.0, 1.06)),
        "owl" => GradeMatrix.Compose(GradeMatrix.Brightness(0.14), GradeMatrix.Contrast(0.86), GradeMatrix.Saturation(1.06)),
        "toxic" => GradeMatrix.Compose(GradeMatrix.Saturation(1.40), GradeMatrix.Gain(0.90, 1.16, 0.90), GradeMatrix.Contrast(1.10)),
        "vapor" => GradeMatrix.Compose(GradeMatrix.Saturation(1.55), GradeMatrix.Gain(1.12, 0.94, 1.14), GradeMatrix.Contrast(1.06)),
        "crimson" => GradeMatrix.Compose(GradeMatrix.Saturation(1.22), GradeMatrix.Gain(1.20, 0.90, 0.90), GradeMatrix.Contrast(1.16), GradeMatrix.Brightness(-0.01)),
        _ => GradeMatrix.Identity()
    };

    /// <summary>The look's grade scaled by strength (0 = off, 1 = full).</summary>
    public static float[] Lerp(string? lookId, double strength01) =>
        GradeMatrix.Lerp(GradeMatrix.Identity(), For(lookId), strength01 < 0 ? 0 : strength01 > 1 ? 1 : strength01);
}
