using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public readonly record struct DebugLine(
    Vector3 Start,
    Vector3 End,
    Vector4 Color);

public readonly record struct DebugLabel(
    Vector3 Position,
    string Text,
    Vector4 Color);

public sealed class DebugDraw
{
    private readonly List<DebugLine> _lines = new();
    private readonly List<DebugLabel> _labels = new();

    public bool Enabled { get; set; }

    public ReadOnlySpan<DebugLine> Lines =>
        CollectionsMarshal.AsSpan(_lines);

    public IReadOnlyList<DebugLabel> Labels => _labels;

    public void Clear()
    {
        _lines.Clear();
        _labels.Clear();
    }

    public void Line(
        Vector3 start,
        Vector3 end,
        Vector4 color)
    {
        if (!Enabled)
        {
            return;
        }

        _lines.Add(new DebugLine(start, end, color));
    }

    public void Box(
        AxisAlignedBounds bounds,
        Vector4 color)
    {
        if (!Enabled)
        {
            return;
        }

        Vector3 min = bounds.Minimum;
        Vector3 max = bounds.Maximum;

        Vector3 p000 = new(min.X, min.Y, min.Z);
        Vector3 p100 = new(max.X, min.Y, min.Z);
        Vector3 p010 = new(min.X, max.Y, min.Z);
        Vector3 p110 = new(max.X, max.Y, min.Z);
        Vector3 p001 = new(min.X, min.Y, max.Z);
        Vector3 p101 = new(max.X, min.Y, max.Z);
        Vector3 p011 = new(min.X, max.Y, max.Z);
        Vector3 p111 = new(max.X, max.Y, max.Z);

        Line(p000, p100, color);
        Line(p100, p110, color);
        Line(p110, p010, color);
        Line(p010, p000, color);

        Line(p001, p101, color);
        Line(p101, p111, color);
        Line(p111, p011, color);
        Line(p011, p001, color);

        Line(p000, p001, color);
        Line(p100, p101, color);
        Line(p010, p011, color);
        Line(p110, p111, color);
    }

    public void Circle(
        Vector3 center,
        float radius,
        Vector4 color,
        int segments = 32)
    {
        if (!Enabled)
        {
            return;
        }

        if (!float.IsFinite(radius) || radius <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }

        if (segments < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(segments));
        }

        float step = MathF.Tau / segments;
        Vector3 previous =
            center + new Vector3(radius, 0.0f, 0.0f);

        for (int index = 1; index <= segments; index++)
        {
            float angle = index * step;
            Vector3 next =
                center +
                new Vector3(
                    MathF.Cos(angle) * radius,
                    0.0f,
                    MathF.Sin(angle) * radius);

            Line(previous, next, color);
            previous = next;
        }
    }

    public void Point(
        Vector3 position,
        float size,
        Vector4 color)
    {
        if (!Enabled)
        {
            return;
        }

        if (!float.IsFinite(size) || size <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        float half = size * 0.5f;
        Line(
            position - Vector3.UnitX * half,
            position + Vector3.UnitX * half,
            color);
        Line(
            position - Vector3.UnitY * half,
            position + Vector3.UnitY * half,
            color);
        Line(
            position - Vector3.UnitZ * half,
            position + Vector3.UnitZ * half,
            color);
    }

    public void Label(
        Vector3 position,
        string text,
        Vector4 color)
    {
        if (!Enabled)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _labels.Add(new DebugLabel(position, text, color));
    }
}
