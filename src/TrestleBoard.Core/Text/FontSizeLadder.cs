namespace TrestleBoard.Core.Text;

/// <summary>
/// The fixed set of text sizes the Bigger/Smaller buttons walk (PLAN.md M14).
/// <para>
/// A ladder rather than arithmetic, so the buttons cannot produce 11.3pt. For an elderly
/// readership "too small" is a more common complaint than "wrong typeface", which is why size
/// ships beside the font picker rather than after it.
/// </para>
/// </summary>
public static class FontSizeLadder
{
    private static readonly float[] Rungs =
        [6f, 7f, 8f, 8.5f, 9f, 10f, 11f, 12f, 14f, 16f, 18f, 20f, 24f, 28f, 32f, 36f, 48f, 60f, 72f];

    public static IReadOnlyList<float> Sizes => Rungs;

    public static float Smallest => Rungs[0];

    public static float Largest => Rungs[^1];

    /// <summary>
    /// The next rung up or down from <paramref name="sizePt"/>, or null when there is none.
    /// A size that sits between two rungs — an older document may hold any value — steps to the
    /// nearest rung in the requested direction rather than snapping first and then stepping.
    /// </summary>
    public static float? Step(float sizePt, int direction)
    {
        if (direction > 0)
        {
            foreach (float rung in Rungs)
            {
                if (rung > sizePt + 0.01f)
                {
                    return rung;
                }
            }

            return null;
        }

        for (int i = Rungs.Length - 1; i >= 0; i--)
        {
            if (Rungs[i] < sizePt - 0.01f)
            {
                return Rungs[i];
            }
        }

        return null;
    }

    /// <summary>The rung nearest a size, for showing the current value in the stepper.</summary>
    public static float Nearest(float sizePt)
    {
        float best = Rungs[0];
        foreach (float rung in Rungs)
        {
            if (Math.Abs(rung - sizePt) < Math.Abs(best - sizePt))
            {
                best = rung;
            }
        }

        return best;
    }
}
