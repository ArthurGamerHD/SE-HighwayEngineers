using System;
using System.Collections.Generic;
using HighwayEngineer.Common.Structures;
using VRageMath;

namespace HighwayEngineer.Client.Roads
{
    /// <summary>
    /// Turns a planned bridge (its signed distance field, the one the server builds voxels from) into a triangle
    /// mesh for the planner's 3D view. The field is sampled on a lattice that follows the deck: one column of
    /// samples per step along it, in the frame of the deck segment there, so a long or curved bridge costs no
    /// more samples than its own length. Surface nets then put one vertex in every lattice cell the surface
    /// crosses and a quad across every lattice edge it cuts. Any thread: it only reads the shape.
    /// </summary>
    internal static class BridgePreviewMesher
    {
        const double MinimumCellMeters = 1.5;
        // Lattice points per bridge. Past it the cells grow, so a very long bridge gets a coarser preview.
        const int MaximumSamples = 160000;
        // The deck runs on this far past its ends, buried (BridgeShape's overhang).
        const double EndMargin = 5.0;

        public sealed class Mesh
        {
            /// <summary>World position the vertices are relative to, so they keep float precision.</summary>
            public Vector3D Origin;
            public Vector3[] Vertices;
            public int[] Indices;
            public Color[] TriangleColors;
        }

        public static Mesh Build(BridgeShape shape, Color structure, Color pavement)
        {
            int segments = shape.SegmentCount;
            if (segments <= 0)
                return null;
            var starts = new double[segments + 1];
            for (int i = 0; i < segments; i++)
                starts[i + 1] = starts[i] + shape.GetSegment(i).Length;
            double length = starts[segments];
            double before = Math.Max(shape.RampReach(false), EndMargin);
            double after = Math.Max(shape.RampReach(true), EndMargin);

            // Each segment's solids in its own frame (X along, Y up, Z side); the lattice spans all of them.
            var bounds = new BoundingBoxD[segments];
            double minY = double.MaxValue, maxY = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            for (int i = 0; i < segments; i++)
            {
                double segmentLength = shape.GetSegment(i).Length;
                bounds[i] = shape.SegmentBounds(i, i == 0 ? -before : 0.0,
                    i == segments - 1 ? segmentLength + after : segmentLength);
                minY = Math.Min(minY, bounds[i].Min.Y);
                maxY = Math.Max(maxY, bounds[i].Max.Y);
                minZ = Math.Min(minZ, bounds[i].Min.Z);
                maxZ = Math.Max(maxZ, bounds[i].Max.Z);
            }

            double span = length + before + after;
            double volume = span * (maxY - minY) * (maxZ - minZ);
            double cell = Math.Max(MinimumCellMeters, Math.Pow(Math.Max(volume, 1.0) / MaximumSamples, 1.0 / 3.0));
            // A cell of margin all around, so the surface closes inside the lattice.
            int nx = (int)Math.Ceiling(span / cell) + 3;
            int ny = (int)Math.Ceiling((maxY - minY) / cell) + 3;
            int nz = (int)Math.Ceiling((maxZ - minZ) / cell) + 3;
            double x0 = -before - cell, y0 = minY - cell, z0 = minZ - cell;

            var origins = new Vector3D[nx];
            var ups = new Vector3D[nx];
            var sides = new Vector3D[nx];
            var values = new float[nx * ny * nz];
            float outside = (float)(cell * 4.0);
            int piece = 0;
            for (int ix = 0; ix < nx; ix++)
            {
                double along = x0 + ix * cell;
                while (piece < segments - 1 && along >= starts[piece + 1])
                    piece++;
                BridgeShape.Segment segment = shape.GetSegment(piece);
                origins[ix] = segment.Start + segment.Forward * (along - starts[piece]);
                ups[ix] = segment.Up;
                sides[ix] = segment.Side;
                BoundingBoxD box = bounds[piece];
                for (int iy = 0; iy < ny; iy++)
                {
                    double y = y0 + iy * cell;
                    bool rowInside = y >= box.Min.Y - cell && y <= box.Max.Y + cell;
                    int row = (ix * ny + iy) * nz;
                    for (int iz = 0; iz < nz; iz++)
                    {
                        double z = z0 + iz * cell;
                        // Nothing of this segment is solid outside its bounds: no need to ask the field.
                        if (!rowInside || z < box.Min.Z - cell || z > box.Max.Z + cell)
                        {
                            values[row + iz] = outside;
                            continue;
                        }
                        Vector3D world = origins[ix] + ups[ix] * y + sides[ix] * z;
                        values[row + iz] = (float)shape.Distance(shape.Locate(world));
                    }
                }
            }

            Vector3D meshOrigin = shape.Axis.Start;
            var vertices = new List<Vector3>();
            var cellVertices = new int[(nx - 1) * (ny - 1) * (nz - 1)];
            var corners = new Vector3D[8];
            var cornerValues = new float[8];
            for (int ix = 0; ix < nx - 1; ix++)
            for (int iy = 0; iy < ny - 1; iy++)
            for (int iz = 0; iz < nz - 1; iz++)
            {
                int inside = 0;
                for (int c = 0; c < 8; c++)
                {
                    int cx = ix + (c & 1), cy = iy + ((c >> 1) & 1), cz = iz + ((c >> 2) & 1);
                    cornerValues[c] = values[(cx * ny + cy) * nz + cz];
                    if (cornerValues[c] < 0f) inside++;
                }
                int cellIndex = (ix * (ny - 1) + iy) * (nz - 1) + iz;
                if (inside == 0 || inside == 8)
                {
                    cellVertices[cellIndex] = -1;
                    continue;
                }
                for (int c = 0; c < 8; c++)
                {
                    int cx = ix + (c & 1);
                    corners[c] = origins[cx] + ups[cx] * (y0 + (iy + ((c >> 1) & 1)) * cell) +
                                 sides[cx] * (z0 + (iz + ((c >> 2) & 1)) * cell);
                }
                // The vertex: the mean of where the surface crosses the cell's twelve edges.
                Vector3D sum = Vector3D.Zero;
                int crossings = 0;
                for (int e = 0; e < 12; e++)
                {
                    int a = EdgeCorners[e * 2], b = EdgeCorners[e * 2 + 1];
                    float va = cornerValues[a], vb = cornerValues[b];
                    if (va < 0f == vb < 0f) continue;
                    double t = va / (double)(va - vb);
                    sum += corners[a] + (corners[b] - corners[a]) * t;
                    crossings++;
                }
                cellVertices[cellIndex] = vertices.Count;
                vertices.Add((Vector3)(sum / crossings - meshOrigin));
            }

            var indices = new List<int>();
            var colors = new List<Color>();
            for (int ix = 0; ix < nx; ix++)
            for (int iy = 0; iy < ny; iy++)
            for (int iz = 0; iz < nz; iz++)
            {
                bool solid = values[(ix * ny + iy) * nz + iz] < 0f;
                // The four cells around each lattice edge the surface cuts share a quad.
                if (ix < nx - 1 && iy > 0 && iy < ny - 1 && iz > 0 && iz < nz - 1 &&
                    solid != values[((ix + 1) * ny + iy) * nz + iz] < 0f)
                    AddQuad(shape, meshOrigin, vertices, indices, colors, structure, pavement,
                        cellVertices[(ix * (ny - 1) + iy - 1) * (nz - 1) + iz - 1],
                        cellVertices[(ix * (ny - 1) + iy) * (nz - 1) + iz - 1],
                        cellVertices[(ix * (ny - 1) + iy) * (nz - 1) + iz],
                        cellVertices[(ix * (ny - 1) + iy - 1) * (nz - 1) + iz]);
                if (iy < ny - 1 && ix > 0 && ix < nx - 1 && iz > 0 && iz < nz - 1 &&
                    solid != values[(ix * ny + iy + 1) * nz + iz] < 0f)
                    AddQuad(shape, meshOrigin, vertices, indices, colors, structure, pavement,
                        cellVertices[((ix - 1) * (ny - 1) + iy) * (nz - 1) + iz - 1],
                        cellVertices[(ix * (ny - 1) + iy) * (nz - 1) + iz - 1],
                        cellVertices[(ix * (ny - 1) + iy) * (nz - 1) + iz],
                        cellVertices[((ix - 1) * (ny - 1) + iy) * (nz - 1) + iz]);
                if (iz < nz - 1 && ix > 0 && ix < nx - 1 && iy > 0 && iy < ny - 1 &&
                    solid != values[(ix * ny + iy) * nz + iz + 1] < 0f)
                    AddQuad(shape, meshOrigin, vertices, indices, colors, structure, pavement,
                        cellVertices[((ix - 1) * (ny - 1) + iy - 1) * (nz - 1) + iz],
                        cellVertices[(ix * (ny - 1) + iy - 1) * (nz - 1) + iz],
                        cellVertices[(ix * (ny - 1) + iy) * (nz - 1) + iz],
                        cellVertices[((ix - 1) * (ny - 1) + iy) * (nz - 1) + iz]);
            }

            if (indices.Count == 0)
                return null;
            return new Mesh
            {
                Origin = meshOrigin,
                Vertices = vertices.ToArray(),
                Indices = indices.ToArray(),
                TriangleColors = colors.ToArray()
            };
        }

        // Corner c of a cell is at (c & 1, (c >> 1) & 1, (c >> 2) & 1).
        static readonly int[] EdgeCorners =
        {
            0, 1, 2, 3, 4, 5, 6, 7,
            0, 2, 1, 3, 4, 6, 5, 7,
            0, 4, 1, 5, 2, 6, 3, 7
        };

        static void AddQuad(BridgeShape shape, Vector3D origin, List<Vector3> vertices, List<int> indices,
            List<Color> colors, Color structure, Color pavement, int a, int b, int c, int d)
        {
            if (a < 0 || b < 0 || c < 0 || d < 0)
                return;
            Vector3D middle = origin + (Vector3D)(vertices[a] + vertices[b] + vertices[c] + vertices[d]) * 0.25;
            Color color = shape.IsPavement(shape.Locate(middle)) ? pavement : structure;
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
            indices.Add(a);
            indices.Add(c);
            indices.Add(d);
            colors.Add(color);
            colors.Add(color);
        }
    }
}
