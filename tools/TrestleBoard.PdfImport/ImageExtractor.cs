using System.Numerics;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.Content;
using PdfSharpCore.Pdf.Content.Objects;
using TrestleBoard.Core.Model;

namespace TrestleBoard.PdfImport;

internal sealed record PlacedImage(PdfDictionary XObject, Matrix3x2 Ctm);

/// <summary>
/// Walks a page's content stream tracking the graphics-state matrix (q/Q/cm) to find where each
/// "Do" operator paints an Image XObject, and reads that image's raw bytes for JPEG (DCTDecode)
/// images — the only format extracted; PDFsharpCore doesn't decode other image filters back to a
/// usable file format, so other encodings are skipped and logged, never silently dropped.
/// Nested Form XObjects are not descended into (out of scope for this best-effort tool).
/// </summary>
internal static class ImageExtractor
{
    public static List<PlacedImage> ExtractPlacedImages(PdfPage page)
    {
        var result = new List<PlacedImage>();
        CSequence content;
        try
        {
            content = ContentReader.ReadContent(page);
        }
        catch (Exception)
        {
            return result;
        }

        PdfDictionary? xObjects = page.Resources?.Elements?.GetDictionary("/XObject");
        var stack = new Stack<Matrix3x2>();
        Matrix3x2 ctm = Matrix3x2.Identity;
        Walk(content, ref ctm, stack, xObjects, result);
        return result;
    }

    private static void Walk(CSequence sequence, ref Matrix3x2 ctm, Stack<Matrix3x2> stack, PdfDictionary? xObjects, List<PlacedImage> result)
    {
        foreach (CObject obj in sequence)
        {
            if (obj is not COperator op)
            {
                continue;
            }

            switch (op.Name)
            {
                case "q":
                    stack.Push(ctm);
                    break;

                case "Q":
                    if (stack.Count > 0)
                    {
                        ctm = stack.Pop();
                    }

                    break;

                case "cm":
                    if (TryReadMatrix(op.Operands, out Matrix3x2 m))
                    {
                        ctm = Matrix3x2.Multiply(m, ctm);
                    }

                    break;

                case "Do":
                    if (xObjects is not null && op.Operands.Count > 0 && op.Operands[0] is CName name)
                    {
                        PdfDictionary? xObject = ResolveXObject(xObjects, name.Name);
                        if (xObject is not null && xObject.Elements.GetName("/Subtype") == "/Image")
                        {
                            result.Add(new PlacedImage(xObject, ctm));
                        }
                    }

                    break;
            }
        }
    }

    private static PdfDictionary? ResolveXObject(PdfDictionary xObjects, string name)
    {
        if (!xObjects.Elements.ContainsKey(name))
        {
            return null;
        }

        PdfItem item = xObjects.Elements[name];
        return item switch
        {
            PdfReference reference => reference.Value as PdfDictionary,
            PdfDictionary dict => dict,
            _ => null,
        };
    }

    private static bool TryReadMatrix(CSequence operands, out Matrix3x2 matrix)
    {
        matrix = Matrix3x2.Identity;
        if (operands.Count < 6)
        {
            return false;
        }

        var v = new float[6];
        for (int i = 0; i < 6; i++)
        {
            v[i] = operands[i] switch
            {
                CReal r => (float)r.Value,
                CInteger n => n.Value,
                _ => float.NaN,
            };
            if (float.IsNaN(v[i]))
            {
                return false;
            }
        }

        matrix = new Matrix3x2(v[0], v[1], v[2], v[3], v[4], v[5]);
        return true;
    }

    /// <summary>Unit square [0,1]x[0,1] mapped through the CTM, axis-aligned bounding box, then
    /// flipped from PDF's y-up/bottom-left space into RectPt's y-down/top-left space.</summary>
    public static RectPt ComputeRect(Matrix3x2 ctm, float pageHeightPt)
    {
        Vector2 p00 = Vector2.Transform(new Vector2(0, 0), ctm);
        Vector2 p10 = Vector2.Transform(new Vector2(1, 0), ctm);
        Vector2 p01 = Vector2.Transform(new Vector2(0, 1), ctm);
        Vector2 p11 = Vector2.Transform(new Vector2(1, 1), ctm);

        float minX = Math.Min(Math.Min(p00.X, p10.X), Math.Min(p01.X, p11.X));
        float maxX = Math.Max(Math.Max(p00.X, p10.X), Math.Max(p01.X, p11.X));
        float minYPdf = Math.Min(Math.Min(p00.Y, p10.Y), Math.Min(p01.Y, p11.Y));
        float maxYPdf = Math.Max(Math.Max(p00.Y, p10.Y), Math.Max(p01.Y, p11.Y));

        float top = pageHeightPt - maxYPdf;
        return new RectPt(minX, top, maxX - minX, maxYPdf - minYPdf);
    }

    public static byte[]? TryGetJpegBytes(PdfDictionary xObject)
    {
        if (GetLastFilterName(xObject) != "/DCTDecode")
        {
            return null;
        }

        return xObject.Stream?.Value;
    }

    private static string GetLastFilterName(PdfDictionary dict)
    {
        if (!dict.Elements.ContainsKey("/Filter"))
        {
            return "";
        }

        PdfItem item = dict.Elements["/Filter"];
        if (item is PdfName singleName)
        {
            return singleName.Value;
        }

        if (item is PdfArray array && array.Elements.Count > 0)
        {
            return (array.Elements[array.Elements.Count - 1] as PdfName)?.Value ?? "";
        }

        return "";
    }
}
