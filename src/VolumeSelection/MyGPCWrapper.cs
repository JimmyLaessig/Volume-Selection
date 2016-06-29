﻿using Aardvark.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Aardvark.VRVis
{
    /// <summary>
    /// This is a wrapper for the GPC library ("A General Polygon Clipping Library"): 
    /// http://www.cs.man.ac.uk/~toby/alan/software/gpc.html. 
    /// 
    /// Polygon Sets (consisting of individual Polygon2ds that are either defined as outer boundaries or holes)
    /// can be clipped using the operations "intersection", "exclusive-or", "union" or "difference".
    /// 
    /// Polygons can be convex, concave, self-intersecting, and can contain holes.
    /// 
    /// The closed Polygons GENERATED by the clipper will NEVER SELF-INTERSECT.
    /// 
    /// If a polygon defined as a hole intersects the boundary polygon, the part of the hole-polygon that
    /// lies outside is treated as a normal "outer boundary" polygon.
    /// </summary>
    public static class GpcWrapper
    {
        public static V2d[][] ComputeTriangleStrip(GpcPolygon polygon)
        {
            gpc_tristrip gpc_strip = new gpc_tristrip();
            gpc_polygon gpc_pol = polygon.ToNative();
            gpc_polygon_to_tristrip(ref gpc_pol, ref gpc_strip);
            var result = gpc_strip.ToManaged();

            GpcWrapper.Free(gpc_pol);
            GpcWrapper.gpc_free_tristrip(ref gpc_strip);

            return result;
        }

        public static V2d[][] ClipToTriangleStrip(GpcOperation operation, GpcPolygon subject_polygon, GpcPolygon clip_polygon)
        {
            gpc_tristrip gpc_strip = new gpc_tristrip();
            gpc_polygon gpc_subject_polygon = subject_polygon.ToNative();
            gpc_polygon gpc_clip_polygon = clip_polygon.ToNative();

            gpc_tristrip_clip(operation, ref gpc_subject_polygon, ref gpc_clip_polygon, ref gpc_strip);
            var result = gpc_strip.ToManaged();

            GpcWrapper.Free(gpc_subject_polygon);
            GpcWrapper.Free(gpc_clip_polygon);
            GpcWrapper.gpc_free_tristrip(ref gpc_strip);

            return result;
        }

        public static GpcPolygon Clip(GpcOperation operation, GpcPolygon subject_polygon, GpcPolygon clip_polygon)
        {
            gpc_polygon gpc_polygon = new gpc_polygon();
            gpc_polygon gpc_subject_polygon = subject_polygon.ToNative();
            gpc_polygon gpc_clip_polygon = clip_polygon.ToNative();

            gpc_polygon_clip(operation, ref gpc_subject_polygon, ref gpc_clip_polygon, ref gpc_polygon);
            var result = gpc_polygon.ToManaged();

            GpcWrapper.Free(gpc_subject_polygon);
            GpcWrapper.Free(gpc_clip_polygon);
            GpcWrapper.gpc_free_polygon(ref gpc_polygon);

            return result;
        }

        private static gpc_polygon ToNative(this GpcPolygon polygon)
        {
            var gpc_pol = new gpc_polygon();
            gpc_pol.num_contours = polygon.Contour.Length;

            var hole = new int[polygon.Contour.Length].SetByIndex(i => polygon.Hole[i] ? 1 : 0);
            gpc_pol.hole = Marshal.AllocCoTaskMem(polygon.Contour.Length * Marshal.SizeOf(typeof(int)));
            Marshal.Copy(hole, 0, gpc_pol.hole, polygon.Contour.Length);

            gpc_pol.contour = Marshal.AllocCoTaskMem(polygon.Contour.Length * Marshal.SizeOf(new gpc_vertex_list()));
            IntPtr ptr = gpc_pol.contour;
            for (int i = 0; i < polygon.Contour.Length; i++)
            {
                gpc_vertex_list gpc_vtx_list = new gpc_vertex_list();
                gpc_vtx_list.num_vertices = polygon.Contour[i].PointCount;
                gpc_vtx_list.vertex = Marshal.AllocCoTaskMem(polygon.Contour[i].PointCount * Marshal.SizeOf(new gpc_vertex()));
                IntPtr ptr2 = gpc_vtx_list.vertex;
                for (int j = 0; j < polygon.Contour[i].PointCount; j++)
                {
                    gpc_vertex gpc_vtx = new gpc_vertex();
                    gpc_vtx.x = polygon.Contour[i][j].X;
                    gpc_vtx.y = polygon.Contour[i][j].Y;
                    Marshal.StructureToPtr(gpc_vtx, ptr2, false);
                    ptr2 = (IntPtr)(((long)ptr2) + Marshal.SizeOf(gpc_vtx));
                }
                Marshal.StructureToPtr(gpc_vtx_list, ptr, false);
                ptr = (IntPtr)(((long)ptr) + Marshal.SizeOf(gpc_vtx_list));
            }

            return gpc_pol;
        }

        private static GpcPolygon ToManaged(this gpc_polygon poly)
        {
            var num_contours = poly.num_contours;

            var result = new GpcPolygon
            {
                Hole = new bool[num_contours],
                Contour = new Polygon2d[num_contours]
            };

            if (num_contours > 0)
            {
                unsafe
                {
                    var pHole = (int*)poly.hole;
                    var pContour = (gpc_vertex_list*)poly.contour;

                    for (int i = 0; i < num_contours; i++)
                    {
                        var pVertexList = pContour + i;
                        var va = new V2d[pVertexList->num_vertices];
                        for (int j = 0; j < pVertexList->num_vertices; j++)
                        {
                            var pVertex = (gpc_vertex*)pVertexList->vertex + j;
                            va[j].X = pVertex->x; va[j].Y = pVertex->y;     // time  80%
                            //va[j] = new V2d(pVertex->x, pVertex->y);      // time 100%
                        }
                        result.Contour[i] = new Polygon2d(va);
                        result.Hole[i] = pHole[i] != 0;
                    }
                }
            }

            return result;
        }

        private static V2d[][] ToManaged(this gpc_tristrip gpc_strip)
        {
            var nofStrips = gpc_strip.num_strips;
            var tristrip = new V2d[nofStrips][];
            IntPtr ptr = gpc_strip.strip;
            for (int i = 0; i < nofStrips; i++)
            {
                gpc_vertex_list gpc_vtx_list = (gpc_vertex_list)Marshal.PtrToStructure(ptr, typeof(gpc_vertex_list));
                var nofVertices = gpc_vtx_list.num_vertices;
                tristrip[i] = new V2d[nofVertices];

                IntPtr ptr2 = gpc_vtx_list.vertex;
                for (int j = 0; j < nofVertices; j++)
                {
                    gpc_vertex gpc_vtx = (gpc_vertex)Marshal.PtrToStructure(ptr2, typeof(gpc_vertex));
                    tristrip[i][j] = new V2d(gpc_vtx.x, gpc_vtx.y);

                    ptr2 = (IntPtr)(((long)ptr2) + Marshal.SizeOf(gpc_vtx));
                }
                ptr = (IntPtr)(((long)ptr) + Marshal.SizeOf(gpc_vtx_list));
            }

            return tristrip;
        }

        private static void Free(gpc_polygon gpc_pol)
        {
            Marshal.FreeCoTaskMem(gpc_pol.hole);
            IntPtr ptr = gpc_pol.contour;
            for (int i = 0; i < gpc_pol.num_contours; i++)
            {
                gpc_vertex_list gpc_vtx_list = (gpc_vertex_list)Marshal.PtrToStructure(ptr, typeof(gpc_vertex_list));
                Marshal.FreeCoTaskMem(gpc_vtx_list.vertex);
                ptr = (IntPtr)(((long)ptr) + Marshal.SizeOf(gpc_vtx_list));
            }
        }


        #region bindings to native dll

        private const string dllName = "GeneralPolygonClipper.dll";

        [DllImport(dllName, EntryPoint = "gpc_polygon_to_tristrip")]
        private static extern void gpc_polygon_to_tristrip([In]     ref gpc_polygon polygon,
                                                           [In, Out] ref gpc_tristrip tristrip);

        [DllImport(dllName, EntryPoint = "gpc_polygon_clip")]
        private static extern void gpc_polygon_clip([In]     GpcOperation set_operation,
                                                    [In]     ref gpc_polygon subject_polygon,
                                                    [In]     ref gpc_polygon clip_polygon,
                                                    [In, Out] ref gpc_polygon result_polygon);

        [DllImport(dllName, EntryPoint = "gpc_tristrip_clip")]
        private static extern void gpc_tristrip_clip([In]     GpcOperation set_operation,
                                                     [In]     ref gpc_polygon subject_polygon,
                                                     [In]     ref gpc_polygon clip_polygon,
                                                     [In, Out] ref gpc_tristrip result_tristrip);

        [DllImport(dllName, EntryPoint = "gpc_free_tristrip")]
        private static extern void gpc_free_tristrip([In] ref gpc_tristrip tristrip);

        [DllImport(dllName, EntryPoint = "gpc_free_polygon")]
        private static extern void gpc_free_polygon([In] ref gpc_polygon polygon);

        private enum gpc_op
        {
            GPC_DIFF = 0,
            GPC_INT = 1,
            GPC_XOR = 2,
            GPC_UNION = 3
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct gpc_vertex
        {
            public double x;            // double            x;
            public double y;            // double            y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct gpc_vertex_list
        {
            public int num_vertices;    // int               num_vertices;
            public IntPtr vertex;       // gpc_vertex       *vertex;      
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct gpc_polygon
        {
            public int num_contours;    // int               num_contours;
            public IntPtr hole;         // int              *hole;
            public IntPtr contour;      // gpc_vertex_list  *contour;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct gpc_tristrip
        {
            public int num_strips;      // int               num_strips;
            public IntPtr strip;        // gpc_vertex_list  *strip;
        }

        #endregion
    }

    public enum GpcOperation
    {
        Difference = 0,
        Intersection = 1,
        XOr = 2,
        Union = 3
    }

    public struct GpcPolygon
    {
        public bool[] Hole;
        public Polygon2d[] Contour;

        public GpcPolygon(V2d[] polygon, bool isHole = false)
        {
            Contour = new Polygon2d[] { new Polygon2d(polygon) };
            Hole = new bool[] { isHole };
        }

        public GpcPolygon(Polygon2d polygon, bool isHole = false)
        {
            Contour = new Polygon2d[] { polygon };
            Hole = new bool[] { isHole };
        }

        public static GpcPolygon Empty
        {
            get
            {
                return new GpcPolygon { Contour = new Polygon2d[0], Hole = new bool[0] };
            }
        }

        public Box2d ComputeBoundingBox2d()
        {
            var box = Box2d.Invalid;

            for (int i = 0; i < Hole.Length; i++)
                if (!Hole[i]) box.ExtendBy(Contour[i].BoundingBox2d);
            return box;
        }


        public Triangle2d[] ComputeTriangulation()
        {
            var tristrips = GpcWrapper.ComputeTriangleStrip(this);
            var totalTriangleCount = tristrips.Select(x => x.Length - 2).Sum();
            var triangles = new Triangle2d[totalTriangleCount];
            int ti = 0;
            foreach (var s in tristrips)
            {
                for (int i = 2; i < s.Length; i++)
                {
                    if (i % 2 == 0)
                        triangles[ti++] = new Triangle2d((V2d)s[i - 2], (V2d)s[i - 1], (V2d)s[i]);
                    else
                        triangles[ti++] = new Triangle2d((V2d)s[i - 1], (V2d)s[i - 2], (V2d)s[i]);
                }
            }
            return triangles;
        }

        public Tup<List<V2d>, List<int>> ComputeTriangulationIndexed()
        {
            var verticesDict = new Dictionary<V2d, int>();
            var vertices = new List<V2d>();
            var indices = new List<int>();

            var tristrip = GpcWrapper.ComputeTriangleStrip(this);

            foreach (var s in tristrip)
            {
                foreach (var p in s)
                {
                    if (!verticesDict.ContainsKey(p))
                    {
                        verticesDict.Add(p, vertices.Count);
                        vertices.Add(p);
                    }
                }
            }

            foreach (var s in tristrip)
            {
                int i0 = verticesDict[s[0]];
                int i1 = verticesDict[s[1]];
                for (int i = 2; i < s.Length; i++)
                {
                    // 0 1 2; 2 1 3; 2 3 4; 4 3 5
                    // fixes winding for polygons
                    var i2 = verticesDict[s[i]];
                    indices.Add(i0); indices.Add(i1); indices.Add(i2);
                    if (i % 2 == 0) i0 = i2;
                    else i1 = i2;
                }
            }

            return new Tup<List<V2d>, List<int>>(vertices, indices);
        }

        public GpcPolygon CopyWithoutHoles()
        {
            var hole = Hole;
            var nonHoleSubset = Contour.Where((x, i) => !hole[i]).ToArray();
            return new GpcPolygon
            {
                Contour = nonHoleSubset,
                Hole = new bool[nonHoleSubset.Length].Set(false)
            };
        }

        public GpcPolygon Scaled(V2d s)
        {
            var contour = Contour;
            var hole = Hole;
            return new GpcPolygon
            {
                Contour = contour.Map(x => x.Scaled(s)),
                Hole = hole.Copy()
            };
        }

        public GpcPolygon Copy()
        {
            var contour = Contour;
            var hole = Hole;
            return new GpcPolygon
            {
                Contour = contour.Map(x => x.Copy()),
                Hole = hole.Copy()
            };
        }
    }

    public static class GpcPolygonExtensions
    {
        /// <summary>
        /// Returns intersection of two arbitrary GpcPolygons.
        /// </summary>
        public static GpcPolygon Intersect(this GpcPolygon self, GpcPolygon other)
        {
            return GpcWrapper.Clip(GpcOperation.Intersection, self, other);
        }

        /// <summary>
        /// Returns difference of two arbitrary GpcPolygons.
        /// </summary>
        public static GpcPolygon Subtract(this GpcPolygon self, GpcPolygon other)
        {
            return GpcWrapper.Clip(GpcOperation.Difference, self, other);
        }

        /// <summary>
        /// Return unification of two arbitrary GpcPolygons.
        /// </summary>
        public static GpcPolygon Unite(this GpcPolygon self, GpcPolygon other)
        {
            return GpcWrapper.Clip(GpcOperation.Union, self, other);
        }

        /// <summary>
        /// Returns XOr operation of two arbitrary GpcPolygons.
        /// </summary>
        public static GpcPolygon XOr(this GpcPolygon self, GpcPolygon other)
        {
            return GpcWrapper.Clip(GpcOperation.XOr, self, other);
        }
    }

    public static class Polygon2dExternalExtensions
    {
        public static GpcPolygon ToGpcPolygon(this Polygon2d polygon, bool isHole = false)
        {
            return new GpcPolygon(polygon, isHole);
        }

        /// <summary>
        /// Returns intersection of two arbitrary (even self-intersecting) polygons.
        /// </summary>
        public static GpcPolygon Intersect(this Polygon2d self, Polygon2d other)
        {
            return GpcWrapper.Clip(GpcOperation.Intersection, self.ToGpcPolygon(), other.ToGpcPolygon());
        }

        /// <summary>
        /// Returns intersection of arbitrary (even self-intersecting) polygon with given box.
        /// </summary>
        public static GpcPolygon Intersect(this Polygon2d self, Box2d other)
        {
            return GpcWrapper.Clip(GpcOperation.Intersection, self.ToGpcPolygon(), other.ToPolygon2dCCW().ToGpcPolygon());
        }

        /// <summary>
        /// Returns difference of two arbitrary (even self-intersecting) polygons
        /// </summary>
        public static GpcPolygon Subtract(this Polygon2d self, Polygon2d other)
        {
            return GpcWrapper.Clip(GpcOperation.Difference, self.ToGpcPolygon(), other.ToGpcPolygon());
        }

        /// <summary>
        /// Returns difference of arbitrary (even self-intersecting) polygon with given box.
        /// </summary>
        public static GpcPolygon Subtract(this Polygon2d self, Box2d other)
        {
            return GpcWrapper.Clip(GpcOperation.Difference, self.ToGpcPolygon(), other.ToPolygon2dCCW().ToGpcPolygon());
        }

        /// <summary>
        /// Returns unification of two arbitrary (even self-intersecting) polygons
        /// </summary>
        public static GpcPolygon Unite(this Polygon2d self, Polygon2d other)
        {
            return GpcWrapper.Clip(GpcOperation.Union, self.ToGpcPolygon(), other.ToGpcPolygon());
        }

        /// <summary>
        /// Returns unification of arbitrary (even self-intersecting) polygon with given box.
        /// </summary>
        public static GpcPolygon Unite(this Polygon2d self, Box2d other)
        {
            return GpcWrapper.Clip(GpcOperation.Union, self.ToGpcPolygon(), other.ToPolygon2dCCW().ToGpcPolygon());
        }

        /// <summary>
        /// Returns XOr operation of two arbitrary (even self-intersecting) polygons
        /// </summary>
        public static GpcPolygon XOr(this Polygon2d self, Polygon2d other)
        {
            return GpcWrapper.Clip(GpcOperation.XOr, self.ToGpcPolygon(), other.ToGpcPolygon());
        }

        /// <summary>
        /// Returns XOr operation of arbitrary (even self-intersecting) polygon with given box.
        /// </summary>
        public static GpcPolygon XOr(this Polygon2d self, Box2d other)
        {
            return GpcWrapper.Clip(GpcOperation.XOr, self.ToGpcPolygon(), other.ToPolygon2dCCW().ToGpcPolygon());
        }
    }
}