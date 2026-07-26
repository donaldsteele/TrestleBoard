namespace TrestleBoard.Core.Model;

/// <summary>Axis-aligned rectangle in typographic points (1/72"), y-down page space.</summary>
public readonly record struct RectPt(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;
}

public readonly record struct SizePt(float Width, float Height);
