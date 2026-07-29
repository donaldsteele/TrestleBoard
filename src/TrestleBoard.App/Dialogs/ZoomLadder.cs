namespace TrestleBoard.App.Dialogs;

/// <summary>
/// The fixed magnification steps <see cref="PositionPhotoWindow"/>'s Zoom in/out buttons walk
/// (PLAN.md M22), mirroring M14's <c>FontSizeLadder</c> shape: a stepped ladder rather than a
/// freeform slider, with a plain-language readout, because a slider is exactly the fine-motor
/// control PLAN.md §6 exists to avoid.
///
/// <para>Rungs start at 100% rather than PLAN.md's illustrative "75/100/150%" — at 100% the whole
/// picture already fits the preview, so there is nothing to zoom out to; every rung above that
/// magnifies the preview to make fine panning easier, which is the only thing zoom does here (the
/// crop window's SIZE never changes in the Position window — only its centre moves).</para>
/// </summary>
internal static class ZoomLadder
{
    private static readonly float[] Rungs = [1f, 1.5f, 2f, 3f, 4f];

    public static float Smallest => Rungs[0];

    public static float Largest => Rungs[^1];

    /// <summary>The next rung up or down from <paramref name="zoom"/>, or null when there is none.</summary>
    public static float? Step(float zoom, int direction)
    {
        if (direction > 0)
        {
            foreach (float rung in Rungs)
            {
                if (rung > zoom + 0.01f)
                {
                    return rung;
                }
            }

            return null;
        }

        for (int i = Rungs.Length - 1; i >= 0; i--)
        {
            if (Rungs[i] < zoom - 0.01f)
            {
                return Rungs[i];
            }
        }

        return null;
    }

    /// <summary>Plain-language readout for the current rung, e.g. "150%".</summary>
    public static string Label(float zoom) => $"{(int)System.MathF.Round(zoom * 100f)}%";
}
