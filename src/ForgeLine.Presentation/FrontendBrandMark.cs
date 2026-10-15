using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace ForgeLine.Presentation;

/// <summary>Embedded vector artwork, independent of fonts and runtime content loading.</summary>
internal static class FrontendBrandMark
{
    internal const float Width = 640, Height = 80;
    internal readonly record struct Vertex(Vector2 Position, Vector4 Color);
    internal static readonly Vertex[] Triangles = Load();

    private static Vertex[] Load()
    {
        using var stream = typeof(FrontendBrandMark).Assembly.GetManifestResourceStream("ForgeLine.MenuWordmark")
            ?? throw new InvalidDataException("Embedded menu wordmark missing.");
        var root = XDocument.Load(stream).Root ?? throw new InvalidDataException("Invalid menu wordmark.");
        var vertices = new List<Vertex>();
        foreach (var shape in root.Elements())
        {
            Vector2[] points;
            float Number(string name) => float.Parse(shape.Attribute(name)!.Value, CultureInfo.InvariantCulture);
            if (shape.Name.LocalName == "rect")
            {
                float x = Number("x"), y = Number("y"), w = Number("width"), h = Number("height");
                points = [new(x,y),new(x+w,y),new(x+w,y+h),new(x,y+h)];
            }
            else if (shape.Name.LocalName == "polygon")
            {
                var numbers = shape.Attribute("points")!.Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                points = Enumerable.Range(0, numbers.Length / 2).Select(i => new Vector2(numbers[i*2], numbers[i*2+1])).ToArray();
            }
            else continue;
            string fill = shape.Attribute("fill")!.Value;
            var color = new Vector4(Convert.ToInt32(fill[1..3],16)/255f, Convert.ToInt32(fill[3..5],16)/255f,
                Convert.ToInt32(fill[5..7],16)/255f, 1);
            foreach (var point in points)
                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || point.X < 0 || point.X > Width || point.Y < 0 || point.Y > Height)
                    throw new InvalidDataException("Menu wordmark escapes its view box.");
            for (int i = 1; i + 1 < points.Length; i++)
            { vertices.Add(new(points[0],color)); vertices.Add(new(points[i],color)); vertices.Add(new(points[i+1],color)); }
        }
        return vertices.ToArray();
    }
}
