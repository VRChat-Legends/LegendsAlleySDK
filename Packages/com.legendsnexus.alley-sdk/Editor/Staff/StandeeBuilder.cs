// Ported from Auto Standee (https://github.com/Sketch494/Auto-Standee, GPL-3.0):
// subject mask, marching squares outline, loop classification, RDP
// simplification and the folding stand, the same pipeline the website booth
// builder previews with, so the Unity standee matches what the community saw.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    // a printed cardboard standee: the picture on the front, raw cardboard on
    // the back and the cut edges, and a folding easel stand behind it. built
    // standing on y = 0 and facing +Z. submesh 0 is the print, 1 the cardboard
    internal static class StandeeBuilder
    {
        public class Settings
        {
            public float height = 1.6f;
            public float threshold = 0.5f;
            public float thickness = 0.012f;
            public bool stand = true;
        }

        public class Result
        {
            public Mesh mesh;
            public float width;
            public float depth;
        }

        private const int MaskResolution = 512;     // MEDIUM accuracy
        private const float Epsilon = 1.2f;         // MEDIUM accuracy, MEDIUM optimisation
        private const int PointBudget = 240;        // (1200 verts - 96 for the stand) / 4.6
        private const float StandWidth = 0.35f;
        private const float StandAngleDeg = 15f;
        private const float StandHinge = 0.72f;
        private const float CardboardTiles = 2f;    // cardboard texture repeats per metre

        // ------------------------------------------------------------ mask

        private class Mask
        {
            public byte[] bits;
            public int w, h;
            public float factor;
        }

        private static Mask MakeMask(Texture2D image, float threshold)
        {
            threshold = Mathf.Clamp(threshold, 0.02f, 0.98f);
            int imgW = image.width, imgH = image.height;
            if (imgW < 4 || imgH < 4) throw new Exception("the cutout picture is too small");
            float factor = Mathf.Max(imgW, imgH) / (float)Mathf.Min(Mathf.Max(imgW, imgH), MaskResolution);
            int w = Mathf.Max(4, Mathf.RoundToInt(imgW / factor));
            int h = Mathf.Max(4, Mathf.RoundToInt(imgH / factor));

            // row 0 is the top of the picture, like the browser canvas
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    px[y * w + x] = image.GetPixelBilinear((x + 0.5f) / w, 1f - (y + 0.5f) / h);
                }
            }

            int n = w * h;
            var mask = new byte[n];
            byte aMin = 255, aMax = 0;
            foreach (Color32 c in px)
            {
                if (c.a < aMin) aMin = c.a;
                if (c.a > aMax) aMax = c.a;
            }

            if (aMin < 128 && aMax > 128)
            {
                int cut = Mathf.RoundToInt(threshold * 255);
                for (int i = 0; i < n; i++) mask[i] = (byte)(px[i].a > cut ? 1 : 0);
            }
            else
            {
                // median background colour from the four corner patches, then an Otsu split
                int p = Mathf.Max(2, Mathf.Min(w, h) / 32);
                var rs = new List<int>();
                var gs = new List<int>();
                var bs = new List<int>();
                void Sample(int x0, int y0)
                {
                    for (int y = y0; y < y0 + p; y++)
                    {
                        for (int x = x0; x < x0 + p; x++)
                        {
                            Color32 c = px[y * w + x];
                            rs.Add(c.r);
                            gs.Add(c.g);
                            bs.Add(c.b);
                        }
                    }
                }
                Sample(0, 0);
                Sample(w - p, 0);
                Sample(0, h - p);
                Sample(w - p, h - p);
                int Median(List<int> list)
                {
                    list.Sort();
                    return list[list.Count >> 1];
                }
                int br = Median(rs), bg = Median(gs), bb = Median(bs);
                var dist = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float dr = px[i].r - br, dg = px[i].g - bg, db = px[i].b - bb;
                    dist[i] = Mathf.Sqrt(dr * dr + dg * dg + db * db) / 441.673f;
                }
                float tol = Otsu(dist) * (threshold * 2f);
                for (int i = 0; i < n; i++) mask[i] = (byte)(dist[i] > tol ? 1 : 0);
            }

            int coverage = 0;
            foreach (byte b in mask) coverage += b;
            if (coverage < n * 1e-4f) throw new Exception("no subject found in the cutout picture");
            if (coverage > n * 0.999f) throw new Exception("the whole cutout picture was taken as the subject");
            return new Mask { bits = mask, w = w, h = h, factor = imgW / (float)w };
        }

        private static float Otsu(float[] values)
        {
            float vmax = values.Max();
            if (vmax <= 1e-6f) throw new Exception("the cutout picture is one flat colour");
            const int bins = 256;
            var hist = new double[bins];
            float scale = (bins - 1) / vmax;
            foreach (float v in values) hist[Mathf.RoundToInt(v * scale)]++;
            double total = values.Length, sumAll = 0;
            for (int b = 0; b < bins; b++) sumAll += (b / scale) * hist[b];
            double wB = 0, sumB = 0, best = -1;
            float threshold = vmax * 0.5f;
            for (int b = 0; b < bins; b++)
            {
                wB += hist[b];
                if (wB == 0) continue;
                double wF = total - wB;
                if (wF == 0) break;
                sumB += (b / scale) * hist[b];
                double mB = sumB / wB, mF = (sumAll - sumB) / wF;
                double between = wB * wF * (mB - mF) * (mB - mF);
                if (between > best)
                {
                    best = between;
                    threshold = b / scale;
                }
            }
            return threshold;
        }

        // ------------------------------------------------------------ outline

        private static readonly Dictionary<char, Vector2Int> Mid = new Dictionary<char, Vector2Int>
        {
            { 'B', new Vector2Int(1, 0) }, { 'R', new Vector2Int(2, 1) }, { 'T', new Vector2Int(1, 2) }, { 'L', new Vector2Int(0, 1) },
        };

        private static readonly string[][] Segments =
        {
            null, new[] { "BL" }, new[] { "RB" }, new[] { "RL" }, new[] { "TR" }, new[] { "BL", "TR" }, new[] { "TB" }, new[] { "TL" },
            new[] { "LT" }, new[] { "BT" }, new[] { "RB", "LT" }, new[] { "RT" }, new[] { "LR" }, new[] { "BR" }, new[] { "LB" }, null,
        };

        private const long Key = 1 << 14;

        private static List<List<Vector2>> TraceLoops(Mask m)
        {
            int w = m.w, h = m.h;
            int At(int x, int y) => x >= 0 && x < w && y >= 0 && y < h ? m.bits[y * w + x] : 0;
            var next = new Dictionary<long, long>();
            var order = new List<long>();
            for (int i = 0; i <= h; i++)
            {
                for (int j = 0; j <= w; j++)
                {
                    int c = At(j - 1, i - 1) | (At(j, i - 1) << 1) | (At(j, i) << 2) | (At(j - 1, i) << 3);
                    string[] segs = Segments[c];
                    if (segs == null) continue;
                    foreach (string seg in segs)
                    {
                        Vector2Int s = Mid[seg[0]], e = Mid[seg[1]];
                        long from = (2 * j + s.x) * Key + (2 * i + s.y);
                        long to = (2 * j + e.x) * Key + (2 * i + e.y);
                        if (!next.ContainsKey(from)) order.Add(from);
                        next[from] = to;
                    }
                }
            }

            var loops = new List<List<Vector2>>();
            var visited = new HashSet<long>();
            foreach (long start in order)
            {
                if (visited.Contains(start)) continue;
                var loop = new List<Vector2>();
                long p = start;
                while (true)
                {
                    visited.Add(p);
                    loop.Add(new Vector2((p / Key) / 2f - 1f, (p % Key) / 2f - 1f));
                    if (!next.TryGetValue(p, out long q))
                    {
                        loop.Clear();
                        break;
                    }
                    p = q;
                    if (p == start) break;
                }
                if (loop.Count >= 3) loops.Add(loop);
            }
            if (loops.Count == 0) throw new Exception("no outline found in the cutout picture");
            return loops;
        }

        private class Region
        {
            public List<Vector2> outer;
            public List<List<Vector2>> holes = new List<List<Vector2>>();
        }

        private static float SignedArea(List<Vector2> loop)
        {
            float a = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 p = loop[i], q = loop[(i + 1) % loop.Count];
                a += p.x * q.y - q.x * p.y;
            }
            return a * 0.5f;
        }

        private static bool Inside(Vector2 pt, List<Vector2> loop)
        {
            bool inside = false;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 a = loop[i], b = loop[(i + 1) % loop.Count];
                if ((a.y <= pt.y) != (b.y <= pt.y))
                {
                    float x = a.x + (pt.y - a.y) / (b.y - a.y) * (b.x - a.x);
                    if (x > pt.x) inside = !inside;
                }
            }
            return inside;
        }

        private static List<Region> Classify(List<List<Vector2>> loops)
        {
            var outers = new List<(List<Vector2> loop, float area)>();
            var holes = new List<(List<Vector2> loop, float area)>();
            for (int i = 0; i < loops.Count; i++)
            {
                int depth = 0;
                for (int k = 0; k < loops.Count; k++) if (k != i && Inside(loops[i][0], loops[k])) depth++;
                float area = Mathf.Abs(SignedArea(loops[i]));
                if (depth % 2 == 0) outers.Add((loops[i], area));
                else holes.Add((loops[i], area));
            }
            if (outers.Count == 0) throw new Exception("the cutout outline could not be read");
            float maxArea = outers.Max(o => o.area);
            var kept = outers.Where(o => o.area >= Mathf.Max(maxArea * 0.01f, 12f)).ToList();
            var regions = kept.Select(o => new Region { outer = o.loop }).ToList();
            foreach (var hole in holes.Where(hh => hh.area >= Mathf.Max(maxArea * 0.002f, 6f)))
            {
                int best = -1;
                float bestArea = float.MaxValue;
                for (int r = 0; r < kept.Count; r++)
                {
                    if (kept[r].area > hole.area && Inside(hole.loop[0], kept[r].loop) && kept[r].area < bestArea)
                    {
                        bestArea = kept[r].area;
                        best = r;
                    }
                }
                if (best >= 0) regions[best].holes.Add(hole.loop);
            }
            return regions;
        }

        // ------------------------------------------------------------ simplify

        private static List<Vector2> RdpOpen(List<Vector2> points, float epsilon)
        {
            int n = points.Count;
            if (n < 3) return points;
            var keep = new bool[n];
            keep[0] = keep[n - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, n - 1));
            while (stack.Count > 0)
            {
                (int a, int b) = stack.Pop();
                if (b - a < 2) continue;
                Vector2 pa = points[a], pb = points[b];
                float dx = pb.x - pa.x, dy = pb.y - pa.y, len = Mathf.Sqrt(dx * dx + dy * dy);
                float maxD = -1;
                int maxI = -1;
                for (int i = a + 1; i < b; i++)
                {
                    Vector2 p = points[i];
                    float d = len < 1e-12f ? Vector2.Distance(p, pa) : Mathf.Abs(dx * (pa.y - p.y) - dy * (pa.x - p.x)) / len;
                    if (d > maxD)
                    {
                        maxD = d;
                        maxI = i;
                    }
                }
                if (maxD > epsilon)
                {
                    keep[maxI] = true;
                    stack.Push((a, maxI));
                    stack.Push((maxI, b));
                }
            }
            return points.Where((_, i) => keep[i]).ToList();
        }

        private static List<Vector2> RdpClosed(List<Vector2> loop, float epsilon)
        {
            if (loop.Count <= 4 || epsilon <= 0) return loop;
            int far = 0;
            float maxD = -1;
            for (int i = 0; i < loop.Count; i++)
            {
                float d = Vector2.Distance(loop[i], loop[0]);
                if (d > maxD)
                {
                    maxD = d;
                    far = i;
                }
            }
            if (far == 0) return loop;
            List<Vector2> first = RdpOpen(loop.GetRange(0, far + 1), epsilon);
            var tail = loop.GetRange(far, loop.Count - far);
            tail.Add(loop[0]);
            List<Vector2> second = RdpOpen(tail, epsilon);
            var output = first.Take(first.Count - 1).Concat(second.Take(second.Count - 1)).ToList();
            return output.Count >= 3 ? output : loop;
        }

        private static List<Vector2> MergeTinyEdges(List<Vector2> loop, float minLen)
        {
            var pts = new List<Vector2>(loop);
            for (int iter = 0; iter < loop.Count; iter++)
            {
                int n = pts.Count;
                if (n <= 4) break;
                int minI = 0;
                float minD = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    float d = Vector2.Distance(pts[i], pts[(i + 1) % n]);
                    if (d < minD)
                    {
                        minD = d;
                        minI = i;
                    }
                }
                if (minD >= minLen) break;
                int bestIdx = (minI + 1) % n;
                float bestDev = float.MaxValue;
                foreach (int cand in new[] { (minI + 1) % n, minI })
                {
                    Vector2 prev = pts[(cand - 1 + n) % n], nxt = pts[(cand + 1) % n];
                    float sx = nxt.x - prev.x, sy = nxt.y - prev.y;
                    float sl = Mathf.Max(Mathf.Sqrt(sx * sx + sy * sy), 1e-12f);
                    float dev = Mathf.Abs(sx * (prev.y - pts[cand].y) - sy * (prev.x - pts[cand].x)) / sl;
                    if (dev < bestDev)
                    {
                        bestDev = dev;
                        bestIdx = cand;
                    }
                }
                pts.RemoveAt(bestIdx);
            }
            return pts;
        }

        private static List<Vector2> Decimate(List<Vector2> loop, int target)
        {
            if (loop.Count <= target || target < 3) return loop;
            var output = new List<Vector2>();
            for (int k = 0; k < target; k++) output.Add(loop[k * loop.Count / target]);
            return output;
        }

        private static List<Region> Simplify(List<Region> regions)
        {
            float minEdge = Mathf.Max(Epsilon * 0.8f, 0.75f);
            List<Region> Run(float eps) => regions
                .Select(r => new Region
                {
                    outer = MergeTinyEdges(RdpClosed(r.outer, eps), minEdge),
                    holes = r.holes.Select(hh => MergeTinyEdges(RdpClosed(hh, eps), minEdge))
                        .Where(hh => hh.Count >= 3 && Mathf.Abs(SignedArea(hh)) > 4).ToList(),
                })
                .Where(r => r.outer.Count >= 3 && Mathf.Abs(SignedArea(r.outer)) > 4)
                .ToList();
            int Count(List<Region> rs) => rs.Sum(r => r.outer.Count + r.holes.Sum(hh => hh.Count));

            float e = Epsilon;
            List<Region> output = Run(e);
            for (int attempt = 0; Count(output) > PointBudget && attempt < 14; attempt++)
            {
                e *= 1.5f;
                List<Region> trial = Run(e);
                if (trial.Count > 0) output = trial;
            }
            int total = Count(output);
            if (total > PointBudget)
            {
                foreach (Region r in output)
                {
                    r.outer = Decimate(r.outer, Mathf.Max(r.outer.Count * PointBudget / total, 3));
                    r.holes = r.holes.Select(hh => Decimate(hh, Mathf.Max(hh.Count * PointBudget / total, 3))).ToList();
                }
            }
            if (output.Count == 0) throw new Exception("simplifying removed the whole cutout outline");
            return output;
        }

        // ------------------------------------------------------------ triangulation

        // outer counter clockwise, holes bridged in, then ear clipping
        private static List<int> Triangulate(List<Vector2> outer, List<List<Vector2>> holes, List<Vector2> verts)
        {
            var poly = new List<int>();
            var outerPts = SignedArea(outer) < 0 ? Enumerable.Reverse(outer).ToList() : outer;
            foreach (Vector2 p in outerPts)
            {
                poly.Add(verts.Count);
                verts.Add(p);
            }

            var holeLists = holes.Select(hh =>
            {
                var pts = SignedArea(hh) > 0 ? Enumerable.Reverse(hh).ToList() : new List<Vector2>(hh);
                var idx = new List<int>();
                foreach (Vector2 p in pts)
                {
                    idx.Add(verts.Count);
                    verts.Add(p);
                }
                return idx;
            }).OrderByDescending(hh => hh.Max(i => verts[i].x)).ToList();

            foreach (List<int> hole in holeLists)
            {
                int m = hole.OrderByDescending(i => verts[i].x).First();
                Vector2 pm = verts[m];
                int bestK = -1;
                float bestD = float.MaxValue;
                for (int k = 0; k < poly.Count; k++)
                {
                    Vector2 pk = verts[poly[k]];
                    float d = (pk - pm).sqrMagnitude;
                    if (d >= bestD) continue;
                    if (CrossesAny(pm, pk, poly, verts) || holeLists.Any(other => CrossesAny(pm, pk, other, verts))) continue;
                    bestD = d;
                    bestK = k;
                }
                if (bestK < 0) continue;
                int start = hole.IndexOf(m);
                var insert = new List<int>();
                for (int s = 0; s <= hole.Count; s++) insert.Add(hole[(start + s) % hole.Count]);
                insert.Add(poly[bestK]);
                poly.InsertRange(bestK + 1, insert);
            }

            var tris = new List<int>();
            var ring = new List<int>(poly);
            int guard = ring.Count * ring.Count;
            while (ring.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < ring.Count; i++)
                {
                    int a = ring[(i - 1 + ring.Count) % ring.Count], b = ring[i], c = ring[(i + 1) % ring.Count];
                    if (Cross(verts[a], verts[b], verts[c]) <= 1e-9f) continue;
                    bool blocked = false;
                    foreach (int o in ring)
                    {
                        Vector2 po = verts[o];
                        if (po == verts[a] || po == verts[b] || po == verts[c]) continue;
                        if (InTriangle(po, verts[a], verts[b], verts[c]))
                        {
                            blocked = true;
                            break;
                        }
                    }
                    if (blocked) continue;
                    tris.Add(a);
                    tris.Add(b);
                    tris.Add(c);
                    ring.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    // a pinch the ear test cannot settle: drop the flattest corner
                    int worst = 0;
                    float worstCross = float.MaxValue;
                    for (int i = 0; i < ring.Count; i++)
                    {
                        float cr = Mathf.Abs(Cross(verts[ring[(i - 1 + ring.Count) % ring.Count]], verts[ring[i]], verts[ring[(i + 1) % ring.Count]]));
                        if (cr < worstCross)
                        {
                            worstCross = cr;
                            worst = i;
                        }
                    }
                    ring.RemoveAt(worst);
                }
            }
            if (ring.Count == 3 && Cross(verts[ring[0]], verts[ring[1]], verts[ring[2]]) > 1e-9f) tris.AddRange(ring);
            return tris;
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
            Cross(a, b, p) >= 0 && Cross(b, c, p) >= 0 && Cross(c, a, p) >= 0;

        private static bool CrossesAny(Vector2 p, Vector2 q, List<int> loop, List<Vector2> verts)
        {
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 a = verts[loop[i]], b = verts[loop[(i + 1) % loop.Count]];
                if (a == p || b == p || a == q || b == q) continue;
                float d1 = Cross(p, q, a), d2 = Cross(p, q, b), d3 = Cross(a, b, p), d4 = Cross(a, b, q);
                if (((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0))) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ mesh

        private class MeshParts
        {
            public readonly List<Vector3> verts = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int> print = new List<int>();
            public readonly List<int> cardboard = new List<int>();

            public void Tri(List<int> target, Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Func<Vector3, Vector2> uv)
            {
                // unity shows the side the cross product points to
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0) (b, c) = (c, b);
                foreach (Vector3 v in new[] { a, b, c })
                {
                    target.Add(verts.Count);
                    verts.Add(v);
                    normals.Add(normal);
                    uvs.Add(uv(v));
                }
            }

            public void Quad(List<int> target, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Func<Vector3, Vector2> uv)
            {
                Tri(target, a, b, c, normal, uv);
                Tri(target, a, c, d, normal, uv);
            }
        }

        private static Vector2 CardboardUv(Vector3 v, Vector3 normal)
        {
            // planar by the face's main axis, so every face shows the board at true scale
            Vector3 n = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            Vector2 uv = n.z >= n.x && n.z >= n.y ? new Vector2(v.x, v.y) : n.x >= n.y ? new Vector2(v.z, v.y) : new Vector2(v.x, v.z);
            return uv * CardboardTiles;
        }

        public static Result Build(Texture2D image, Settings settings)
        {
            Mask m = MakeMask(image, settings.threshold);
            List<Region> regions = Simplify(Classify(TraceLoops(m)));
            foreach (Region r in regions)
            {
                r.outer = r.outer.Select(p => p * m.factor).ToList();
                r.holes = r.holes.Select(hh => hh.Select(p => p * m.factor).ToList()).ToList();
            }

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector2 p in regions.SelectMany(r => r.outer))
            {
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            float s = settings.height / Mathf.Max(maxY - minY, 1e-6f);
            float cx = (minX + maxX) / 2f;
            float width = (maxX - minX) * s;
            float t = settings.thickness;
            int imgW = image.width, imgH = image.height;

            // picture pixels (y down) to metres. the browser preview is right
            // handed, unity is left handed, so x flips to keep the print readable from the front
            Vector2 ToLocal(Vector2 p) => new Vector2(-(p.x - cx) * s, (maxY - p.y) * s);
            Vector2 PrintUv(Vector3 v) => new Vector2((-v.x / s + cx) / imgW, 1f - (maxY - v.y / s) / imgH);

            var parts = new MeshParts();
            foreach (Region r in regions)
            {
                var verts = new List<Vector2>();
                List<int> tris = Triangulate(r.outer.Select(ToLocal).ToList(), r.holes.Select(hh => hh.Select(ToLocal).ToList()).ToList(), verts);
                for (int i = 0; i < tris.Count; i += 3)
                {
                    Vector2 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                    parts.Tri(parts.print, new Vector3(a.x, a.y, t / 2), new Vector3(b.x, b.y, t / 2), new Vector3(c.x, c.y, t / 2), Vector3.forward, v => PrintUv(v));
                    parts.Tri(parts.cardboard, new Vector3(a.x, a.y, -t / 2), new Vector3(b.x, b.y, -t / 2), new Vector3(c.x, c.y, -t / 2), Vector3.back, v => CardboardUv(v, Vector3.back));
                }

                // cut edges, facing away from the board
                foreach ((List<Vector2> loop, bool hole) in new[] { (r.outer, false) }.Concat(r.holes.Select(hh => (hh, true))))
                {
                    var local = loop.Select(ToLocal).ToList();
                    float sign = Mathf.Sign(SignedArea(local)) * (hole ? -1f : 1f);
                    for (int i = 0; i < local.Count; i++)
                    {
                        Vector2 a = local[i], b = local[(i + 1) % local.Count];
                        Vector2 d = b - a;
                        if (d.sqrMagnitude < 1e-12f) continue;
                        var normal = new Vector3(d.y, -d.x, 0).normalized * sign;
                        parts.Quad(parts.cardboard,
                            new Vector3(a.x, a.y, t / 2), new Vector3(b.x, b.y, t / 2), new Vector3(b.x, b.y, -t / 2), new Vector3(a.x, a.y, -t / 2),
                            normal, v => CardboardUv(v, normal));
                    }
                }
            }

            float depth = t;
            if (settings.stand)
            {
                (float lo, float hi, float cxSlot, float hinge)? slot = FindStandSlot(regions, minY, maxY, x => -(x - cx) * s);
                if (slot.HasValue) depth = BuildStand(parts, settings, width, slot.Value);
            }

            var mesh = new Mesh { name = "Cardboard Cutout" };
            mesh.indexFormat = parts.verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(parts.verts);
            mesh.SetNormals(parts.normals);
            mesh.SetUVs(0, parts.uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(parts.print, 0);
            mesh.SetTriangles(parts.cardboard, 1);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return new Result { mesh = mesh, width = width, depth = depth };
        }

        // ------------------------------------------------------------ stand

        // widest solid span at the hinge height, so the stand hides behind the silhouette
        private static (float lo, float hi, float cx, float hinge)? FindStandSlot(List<Region> regions, float minY, float maxY, Func<float, float> toX)
        {
            float spanY = maxY - minY;
            float hinge = StandHinge;
            for (int attempt = 0; attempt < 14 && hinge >= 0.3f; attempt++, hinge -= 0.045f)
            {
                float yPx = maxY - hinge * spanY;
                var xs = new List<float>();
                foreach (List<Vector2> loop in regions.SelectMany(r => new[] { r.outer }.Concat(r.holes)))
                {
                    for (int i = 0; i < loop.Count; i++)
                    {
                        Vector2 a = loop[i], b = loop[(i + 1) % loop.Count];
                        if ((a.y <= yPx) != (b.y <= yPx)) xs.Add(a.x + (yPx - a.y) / (b.y - a.y) * (b.x - a.x));
                    }
                }
                xs.Sort();
                float bestLo = 0, bestHi = 0;
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    float lo = Mathf.Min(toX(xs[i]), toX(xs[i + 1])), hi = Mathf.Max(toX(xs[i]), toX(xs[i + 1]));
                    if (hi - lo > bestHi - bestLo)
                    {
                        bestLo = lo;
                        bestHi = hi;
                    }
                }
                float usable = (bestHi - bestLo) * 0.92f;
                if (usable >= 0.05f)
                {
                    float mid = (bestLo + bestHi) / 2f;
                    return (mid - usable / 2f, mid + usable / 2f, mid, hinge);
                }
            }
            return null;
        }

        private static float BuildStand(MeshParts parts, Settings settings, float width, (float lo, float hi, float cx, float hinge) slot)
        {
            float t = settings.thickness;
            float height = settings.height;
            float angle = StandAngleDeg * Mathf.Deg2Rad;
            float sinA = Mathf.Sin(angle), cosA = Mathf.Cos(angle);
            float gap = Mathf.Max(0.0004f, t * 0.06f);
            float standW = Mathf.Min(Mathf.Max(StandWidth * width, 0.02f), slot.hi - slot.lo, width * 0.95f);
            float w2 = standW / 2f;
            float hingeY = slot.hinge * height;
            float offsetX = Mathf.Clamp(slot.cx, slot.lo + w2, slot.hi - w2);
            float flapH = Mathf.Max(0.08f * height, t * 3f);
            float legLen = hingeY / Mathf.Max(cosA, 1e-4f);

            // hinge flap glued flush behind the board
            float zFlap = -(t / 2f + gap);
            Prism(parts, new[]
            {
                new Vector3(offsetX - w2, hingeY, zFlap), new Vector3(offsetX + w2, hingeY, zFlap),
                new Vector3(offsetX + w2, hingeY + flapH, zFlap), new Vector3(offsetX - w2, hingeY + flapH, zFlap),
            }, new Vector3(0, 0, -t));

            // easel leg, tapered, leaning back from the hinge to the floor
            float zLeg = -(t / 2f + gap + t + gap);
            Vector3 Leg(float x, float y) => new Vector3(offsetX + x, hingeY + y * cosA, zLeg + y * sinA);
            Prism(parts, new[]
            {
                Leg(-w2, 0), Leg(w2, 0), Leg(0.35f * standW, -legLen), Leg(-0.35f * standW, -legLen),
            }, new Vector3(0, t * sinA, -t * cosA));

            // foot pad flat on the floor behind the leg's contact line
            float zFootFront = zLeg - legLen * sinA - t * cosA - gap;
            float footLen = Mathf.Max(0.05f * height, t * 3f);
            float footHalf = 0.35f * standW * 0.9f;
            Prism(parts, new[]
            {
                new Vector3(offsetX - footHalf, 0, zFootFront), new Vector3(offsetX + footHalf, 0, zFootFront),
                new Vector3(offsetX + footHalf, 0, zFootFront - footLen), new Vector3(offsetX - footHalf, 0, zFootFront - footLen),
            }, new Vector3(0, t, 0));

            return t / 2f - (zFootFront - footLen);
        }

        // a convex outline pushed along `extrude`, all cardboard
        private static void Prism(MeshParts parts, Vector3[] cap, Vector3 extrude)
        {
            Vector3 centre = cap.Aggregate(Vector3.zero, (a, b) => a + b) / cap.Length + extrude / 2f;
            Vector3 dir = extrude.normalized;
            Vector3 capNormal = Vector3.Cross(cap[1] - cap[0], cap[2] - cap[0]).normalized;
            if (Vector3.Dot(capNormal, dir) > 0) capNormal = -capNormal;
            for (int i = 1; i + 1 < cap.Length; i++)
            {
                parts.Tri(parts.cardboard, cap[0], cap[i], cap[i + 1], capNormal, v => CardboardUv(v, capNormal));
                parts.Tri(parts.cardboard, cap[0] + extrude, cap[i] + extrude, cap[i + 1] + extrude, -capNormal, v => CardboardUv(v, capNormal));
            }
            for (int i = 0; i < cap.Length; i++)
            {
                Vector3 a = cap[i], b = cap[(i + 1) % cap.Length];
                Vector3 normal = Vector3.Cross(b - a, extrude).normalized;
                Vector3 mid = (a + b) / 2f + extrude / 2f;
                if (Vector3.Dot(normal, mid - centre) < 0) normal = -normal;
                parts.Quad(parts.cardboard, a, b, b + extrude, a + extrude, normal, v => CardboardUv(v, normal));
            }
        }
    }
}
