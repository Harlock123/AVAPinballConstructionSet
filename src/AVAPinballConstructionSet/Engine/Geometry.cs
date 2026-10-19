using System.Text.Json.Serialization;

namespace AVAPinballConstructionSet.Engine;

/// <summary>A point or a direction on the table. Table coordinates run like the screen's: x to the right, y down.</summary>
public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator -(Vec a) => new(-a.X, -a.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public static Vec operator *(double k, Vec a) => new(a.X * k, a.Y * k);
    public static Vec operator /(Vec a, double k) => new(a.X / k, a.Y / k);

    public double Dot(Vec b) => X * b.X + Y * b.Y;
    public double Cross(Vec b) => X * b.Y - Y * b.X;
    [JsonIgnore] public double Length => Math.Sqrt(X * X + Y * Y);
    [JsonIgnore] public double LengthSquared => X * X + Y * Y;
    /// <summary>The same direction, one long (straight up if there's no direction at all).</summary>
    [JsonIgnore] public Vec Unit => Length is > 1e-12 and var l ? this / l : new Vec(0, -1);
    /// <summary>A quarter turn: the way a point on a wheel turning clockwise on the screen moves.</summary>
    [JsonIgnore] public Vec Perp => new(-Y, X);
    [JsonIgnore] public double Angle => Math.Atan2(Y, X);

    public Vec Rotate(double radians)
    {
        double c = Math.Cos(radians), s = Math.Sin(radians);
        return new Vec(X * c - Y * s, X * s + Y * c);
    }

    public override string ToString() => FormattableString.Invariant($"({X:0.###}, {Y:0.###})");

    public static Vec Polar(double radians, double length = 1) => new(Math.Cos(radians) * length, Math.Sin(radians) * length);
    public static double Distance(Vec a, Vec b) => (a - b).Length;
}

public enum ShapeKind { Segment, Circle, Arc }

/// <summary>How a surface answers the ball: a plain wall, live rubber, a pop bumper's kick, a slingshot's, a flipper's bat.</summary>
public enum Surface { Wall, Rubber, Bumper, Sling, Flipper, Target, Plunger }

/// <summary>
/// One piece of what the ball can hit: a line, a circle (its rim) or an arc of a circle. A shape that moves (a
/// flipper's) turns about a pivot, so every point of it has a velocity of its own. A gate stops the ball only from
/// one side.
/// </summary>
public struct Shape
{
    public ShapeKind Kind;
    /// <summary>A line's ends; a circle's or an arc's centre is A.</summary>
    public Vec A, B;
    public double R;
    /// <summary>An arc runs from Start through Sweep radians (clockwise on the screen).</summary>
    public double Start, Sweep;
    public Surface Surface;
    /// <summary>The part it belongs to (-1: the table's frame).</summary>
    public int Part;
    public Vec Pivot;
    public double Omega;
    /// <summary>A gate stops only a ball on this side of it.</summary>
    public Vec? GateSide;

    public static Shape Line(Vec a, Vec b, Surface s = Surface.Wall, int part = -1) => new() { Kind = ShapeKind.Segment, A = a, B = b, Surface = s, Part = part };
    public static Shape Ring(Vec c, double r, Surface s = Surface.Wall, int part = -1) => new() { Kind = ShapeKind.Circle, A = c, R = r, Surface = s, Part = part };
    public static Shape Bow(Vec c, double r, double start, double sweep, Surface s = Surface.Wall, int part = -1) =>
        new() { Kind = ShapeKind.Arc, A = c, R = r, Start = start, Sweep = sweep, Surface = s, Part = part };

    /// <summary>How fast the surface is moving at a point on it.</summary>
    public readonly Vec Velocity(Vec q) => Omega == 0 ? default : (q - Pivot).Perp * Omega;
}

/// <summary>
/// The ball against the shapes: where a moving ball first touches one (so it can never pass through, however fast
/// it goes), and how far into one a ball is (to push it back out of a moving flipper).
/// </summary>
public static class Collide
{
    public static Vec ClosestOnSegment(Vec a, Vec b, Vec p)
    {
        var e = b - a;
        double l2 = e.LengthSquared;
        if (l2 < 1e-12) return a;
        double t = Math.Clamp((p - a).Dot(e) / l2, 0, 1);
        return a + e * t;
    }

    public static double SegmentDistance(Vec a, Vec b, Vec p) => (p - ClosestOnSegment(a, b, p)).Length;

    /// <summary>Whether an angle lies on an arc.</summary>
    public static bool OnArc(double angle, double start, double sweep)
    {
        double d = (angle - start) % (2 * Math.PI);
        if (d < 0) d += 2 * Math.PI;
        return d <= sweep + 1e-9;
    }

    public static (Vec From, Vec To) ArcEnds(in Shape s) => (s.A + Vec.Polar(s.Start, s.R), s.A + Vec.Polar(s.Start + s.Sweep, s.R));

    /// <summary>The nearest point of a shape to p, the direction from it to p, and how far away p is.</summary>
    public static (Vec Q, Vec N, double D) Nearest(in Shape s, Vec p)
    {
        switch (s.Kind)
        {
            case ShapeKind.Circle:
            {
                var d = p - s.A;
                double l = d.Length;
                var n = l > 1e-9 ? d / l : new Vec(0, -1);
                return (s.A + n * s.R, n, l - s.R);
            }
            case ShapeKind.Segment:
            {
                var q = ClosestOnSegment(s.A, s.B, p);
                var d = p - q;
                double l = d.Length;
                return (q, l > 1e-9 ? d / l : (s.B - s.A).Perp.Unit, l);
            }
            default:
            {
                var d = p - s.A;
                Vec q;
                if (OnArc(d.Angle, s.Start, s.Sweep)) q = s.A + d.Unit * s.R;
                else
                {
                    var (e0, e1) = ArcEnds(s);
                    q = (p - e0).LengthSquared < (p - e1).LengthSquared ? e0 : e1;
                }
                var v = p - q;
                double l = v.Length;
                return (q, l > 1e-9 ? v / l : d.Unit, l);
            }
        }
    }

    /// <summary>When (as a fraction of d, from 0 up to t) a ball moving out from outside a circle of radius R first touches it.</summary>
    private static double Enter(Vec p, Vec d, Vec c, double R)
    {
        var m = p - c;
        double a = d.LengthSquared, b = m.Dot(d), cc = m.LengthSquared - R * R;
        if (a < 1e-12 || cc <= 0 || b >= 0) return double.NaN;
        double disc = b * b - a * cc;
        if (disc < 0) return double.NaN;
        double t = (-b - Math.Sqrt(disc)) / a;
        return t >= 0 ? t : double.NaN;
    }

    /// <summary>When a ball inside a circle of radius R moving along d reaches it from inside.</summary>
    private static double Leave(Vec p, Vec d, Vec c, double R)
    {
        var m = p - c;
        double a = d.LengthSquared, b = m.Dot(d), cc = m.LengthSquared - R * R;
        if (a < 1e-12 || cc >= 0 || b <= 0) return double.NaN;
        double t = (-b + Math.Sqrt(b * b - a * cc)) / a;
        return t >= 0 ? t : double.NaN;
    }

    private static bool Take(double hit, ref double t, ref Vec n, Vec normal)
    {
        if (double.IsNaN(hit) || hit >= t) return false;
        t = hit;
        n = normal;
        return true;
    }

    /// <summary>
    /// A ball of radius r at p moving along d: if it touches the shape before the fraction t of the way, t becomes
    /// when, and n the direction from the shape to the ball there. A ball already touching it is left to <see cref="Nearest"/>.
    /// </summary>
    public static bool Sweep(in Shape s, Vec p, Vec d, double r, ref double t, ref Vec n)
    {
        bool found = false;
        switch (s.Kind)
        {
            case ShapeKind.Circle:
            {
                double hit = Enter(p, d, s.A, s.R + r);
                if (!double.IsNaN(hit)) found = Take(hit, ref t, ref n, (p + d * hit - s.A).Unit);
                break;
            }
            case ShapeKind.Segment:
            {
                var e = s.B - s.A;
                double len = e.Length;
                if (len > 1e-9)
                {
                    var u = e / len;
                    var nn = u.Perp;
                    double side = (p - s.A).Dot(nn);
                    if (side < 0) { nn = -nn; side = -side; }
                    if (s.GateSide is { } g && nn.Dot(g) <= 0) return false;
                    double dn = d.Dot(nn);
                    if (side >= r && dn < 0)
                    {
                        double hit = (r - side) / dn;
                        if (hit < t)
                        {
                            double along = (p + d * hit - s.A).Dot(u);
                            if (along >= 0 && along <= len) found = Take(hit, ref t, ref n, nn);
                        }
                    }
                    // A gate's ends are the walls it hangs between.
                    if (s.GateSide != null) break;
                }
                found |= Point(s.A, p, d, r, ref t, ref n);
                found |= Point(s.B, p, d, r, ref t, ref n);
                break;
            }
            default:
            {
                double outer = Enter(p, d, s.A, s.R + r);
                if (!double.IsNaN(outer) && outer < t)
                {
                    var q = p + d * outer - s.A;
                    if (OnArc(q.Angle, s.Start, s.Sweep)) found |= Take(outer, ref t, ref n, q.Unit);
                }
                if (s.R > r)
                {
                    double inner = Leave(p, d, s.A, s.R - r);
                    if (!double.IsNaN(inner) && inner < t)
                    {
                        var q = p + d * inner - s.A;
                        if (OnArc(q.Angle, s.Start, s.Sweep)) found |= Take(inner, ref t, ref n, -q.Unit);
                    }
                }
                var (e0, e1) = ArcEnds(s);
                found |= Point(e0, p, d, r, ref t, ref n);
                found |= Point(e1, p, d, r, ref t, ref n);
                break;
            }
        }
        return found;
    }

    /// <summary>The ball against a single point: a line's end.</summary>
    private static bool Point(Vec c, Vec p, Vec d, double r, ref double t, ref Vec n)
    {
        double hit = Enter(p, d, c, r);
        return !double.IsNaN(hit) && Take(hit, ref t, ref n, (p + d * hit - c).Unit);
    }

    /// <summary>Whether a ball at p is on the side of a gate it stops.</summary>
    public static bool GateStops(in Shape s, Vec p) => s.GateSide is not { } g || (p - s.A).Dot(g) > 0;

    /// <summary>The circle through three points (null if they're in a line).</summary>
    public static (Vec Centre, double R)? Circle(Vec a, Vec b, Vec c)
    {
        double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Math.Abs(d) < 1e-6) return null;
        double a2 = a.LengthSquared, b2 = b.LengthSquared, c2 = c.LengthSquared;
        var centre = new Vec((a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d, (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d);
        double r = (a - centre).Length;
        return r > 4000 ? null : (centre, r);
    }

    /// <summary>The arc from a through m to b, as a centre, a radius, a start and a sweep; null if they're in a line.</summary>
    public static (Vec Centre, double R, double Start, double Sweep)? Arc(Vec a, Vec m, Vec b)
    {
        if (Circle(a, m, b) is not { } c) return null;
        double sa = (a - c.Centre).Angle, sm = (m - c.Centre).Angle, sb = (b - c.Centre).Angle;
        double Fwd(double from, double to)
        {
            double x = (to - from) % (2 * Math.PI);
            return x < 0 ? x + 2 * Math.PI : x;
        }
        // Clockwise from a to b if m lies on the way; otherwise clockwise from b to a.
        return Fwd(sa, sm) <= Fwd(sa, sb) ? (c.Centre, c.R, sa, Fwd(sa, sb)) : (c.Centre, c.R, sb, Fwd(sb, sa));
    }

    /// <summary>Whether a point is inside a polygon.</summary>
    public static bool Inside(IReadOnlyList<Vec> poly, Vec p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    /// <summary>Whether the path from p to q crosses the line from a to b.</summary>
    public static bool Crosses(Vec p, Vec q, Vec a, Vec b)
    {
        var r = q - p;
        var s = b - a;
        double den = r.Cross(s);
        if (Math.Abs(den) < 1e-12) return false;
        double t = (a - p).Cross(s) / den, u = (a - p).Cross(r) / den;
        return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }
}
