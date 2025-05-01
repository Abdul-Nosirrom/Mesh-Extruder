using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.Splines;

namespace FS.MeshProcessing
{
    public enum Axis
    {
        X, Y, Z
    }
    
    public static class MeshProcessing
    {
        public static Bounds GetBoundingBox(Vector3[] vertices, Matrix4x4? transform = null)
        {
            transform ??= Matrix4x4.identity;

            Bounds bounds = new Bounds();

            Vector3 min = float.MaxValue * Vector3.one;
            Vector3 max = float.MinValue * Vector3.one;

            foreach (var vertex in vertices)
            {
                min = Vector3.Min(min, transform.Value.MultiplyPoint(vertex));
                max = Vector3.Max(max, transform.Value.MultiplyPoint(vertex));
            }

            bounds.min = min;
            bounds.max = max;

            return bounds;
        }

        public struct ExtrusionSettings
        {
            public bool BuildStartCap;
            public bool BuildEndCap;
            public bool FlipNormals;
            public Vector2 UVTilingPerMeter;
            public Vector2 UVOffset;

            public ExtrusionSettings(bool buildStartCap, bool buildEndCap, bool flipNormals, Vector2 uVTilingPerMeter, Vector2 uvOffset)
            {
                this.BuildStartCap = buildStartCap;
                this.BuildEndCap = buildEndCap;
                this.UVTilingPerMeter = uVTilingPerMeter;
                this.UVOffset = uvOffset;
                this.FlipNormals = flipNormals;
            }
            
            public static ExtrusionSettings Default 
                => new(true, true, false, new Vector2(1, 1), Vector2.zero);
        }

        public static void ExtrudeMesh(Mesh srcMesh, Mesh dstMesh, Matrix4x4[] extrusionSegments, 
            ExtrusionSettings settings, float smoothingAngle = 45f)
        {
            // NOTE: for the boundary groups, lets just do a weld specifically when getting the manifold edges, like adding that as an option
            var halfEdges = HalfEdge.BakeHalfEdges(srcMesh.triangles);
            var boundaryGroups = HalfEdge.BuildBoundarySmoothingGroups(srcMesh.vertices, halfEdges, smoothingAngle);
            ExtrudeMesh(srcMesh, dstMesh, extrusionSegments, boundaryGroups, settings);
        }

        public static void ExtrudeMesh(Mesh srcMesh, Mesh dstMesh, Matrix4x4[] extrusionSegments,
            List<List<HalfEdge>> boundaryGroups, ExtrusionSettings settings)
        {
            List<Vector3> vertices = new();
            List<int> indices = new();
            List<Vector2> uvs = new();
            List<Color> colors = new();

            int capIdxOffset = 0;
            // Apply Caps
            {
                void BuildCaps(bool startCap)
                {
                    int idxOffset = startCap ? 0 : srcMesh.vertices.Length;
                    Matrix4x4 extrusionMatrix = extrusionSegments[startCap ? 0 : ^1];
                    foreach (var vert in srcMesh.vertices)
                    {
                        var extrudedVert = extrusionMatrix.MultiplyPoint(vert);
                        vertices.Add(extrudedVert);
                    }
                    for (int triIdx = 0; triIdx < srcMesh.triangles.Length; triIdx+=3)
                    {
                        int idxA = srcMesh.triangles[triIdx];
                        int idxB = srcMesh.triangles[triIdx + 1];
                        int idxC = srcMesh.triangles[triIdx + 2];
                        
                        if (settings.FlipNormals)
                            (idxB, idxC) = (idxC, idxB);

                        indices.Add(idxA + idxOffset);
                        indices.Add((startCap ? idxB : idxC) + idxOffset);
                        indices.Add((startCap ? idxC : idxB) + idxOffset);
                    }
                
                    uvs.AddRange(srcMesh.uv);
                    colors.AddRange(srcMesh.colors);
                    
                    capIdxOffset += srcMesh.vertices.Length;
                }
            
                // Build caps first thing
                if (settings.BuildStartCap) BuildCaps(true);

                if (settings.BuildEndCap) BuildCaps(false);
            }
            
            
            // Build Extrusion Mesh
            int subMeshOffset = capIdxOffset;
            for (int smoothingIdx = 0; smoothingIdx < boundaryGroups.Count; smoothingIdx++)
            {
                var smoothingGroup = boundaryGroups[smoothingIdx];
                for (int xe = 0; xe < extrusionSegments.Length; xe++)
                {
                    Matrix4x4 extrusionMatrix = extrusionSegments[xe];
                    int idxCount = smoothingGroup.Count;
                    
                    for (int be = 0; be < smoothingGroup.Count; be++)
                    {
                        var edge = smoothingGroup[be];
                        var va = extrusionMatrix.MultiplyPoint(srcMesh.vertices[edge.Vertex]);
                        
                        // Calculate UV (U is based on extrusion segment, V is based on linear distance along the smoothing group edge)
                        // V CAN be expensive to calculate tho, we can just cache it earlier on but still...
                        {
                            var p1 = be == 0 ? va : vertices[^1];
                            var p2 = va;

                            Vector2 uv = new Vector2(0, 0);
                            uv.y = Vector3.Distance(p1, p2);
                            uv.x = xe;//Vector3.Distance(p1, p2);
                            
                            if (be != 0) 
                            {
                                uv.y += uvs[^1].y;
                            }
                            
                            uvs.Add(uv);
                        }
                        vertices.Add(va);

                        // Triangles for it were set by prev iteration, we just add the vertex data and dip
                        if (xe == extrusionSegments.Length - 1 || be == smoothingGroup.Count - 1) continue;
                        
                        // Quad per extrusion segment connecting this edge with adjacent edge
                        indices.Add(be + xe * idxCount + subMeshOffset);
                        indices.Add(be + (xe + 1) * idxCount + subMeshOffset);
                        indices.Add((be + 1) + (xe + 1) * idxCount + subMeshOffset);

                        if (settings.FlipNormals)
                            (indices[^1], indices[^2]) = (indices[^2], indices[^1]);
                        
                        indices.Add(be + xe * idxCount + subMeshOffset);
                        indices.Add((be + 1) + (xe + 1) * idxCount + subMeshOffset);
                        indices.Add(be + 1 + xe * idxCount + subMeshOffset);
                        
                        if (settings.FlipNormals)
                            (indices[^1], indices[^2]) = (indices[^2], indices[^1]);
                    }
                }
            
                subMeshOffset += smoothingGroup.Count * extrusionSegments.Length;
            }
            
            // Apply
            dstMesh.Clear();
            if (vertices.Count > UInt16.MaxValue)
            {
                dstMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            dstMesh.vertices = vertices.ToArray();
            dstMesh.triangles = indices.ToArray();
            dstMesh.uv = uvs.ToArray();
            // dstMesh.colors = colors.ToArray();
            
            dstMesh.Optimize();
            dstMesh.RecalculateNormals();
            dstMesh.RecalculateBounds();
        }
    }
}