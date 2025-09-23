using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace FS.MeshProcessing
{
    public readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        public readonly int a;
        public readonly int b;
        
        public EdgeKey(int a, int b)
        {
            this.a = a;
            this.b = b;
        }
        
        public override int GetHashCode()
        {
            return a.GetHashCode() ^ b.GetHashCode();
        }

        public bool Equals(EdgeKey edge)
        {
            return (a == edge.a && b == edge.b) || (a == edge.b && b == edge.a);
        }
    }
    
    [Serializable]
    public class HalfEdge : IEquatable<HalfEdge>
    {
        [SerializeField] private int m_vertex;
        public int Vertex => m_vertex;

        [SerializeField] private int m_face;
        public int Face => m_face;
        
        [SerializeReference, HideInInspector] private HalfEdge m_next;
        public HalfEdge Next => m_next;
        [SerializeReference, HideInInspector] private HalfEdge m_prev;
        public HalfEdge Prev => m_prev;
        
        [SerializeReference, HideInInspector] private HalfEdge m_twin;
        public HalfEdge Twin => m_twin;

        [SerializeField] private int m_edgeIndex = -1;
        public int EdgeIndex => m_edgeIndex;
        

        public bool IsBoundary => Twin == null;

        /// <summary>
        /// Generates a half-edge representation of a mesh given its index buffer/triangles.
        /// </summary>
        /// <param name="triangles"></param>
        /// <returns></returns>
        public static HalfEdge[] BakeHalfEdges(int[] triangles)
        {
            HalfEdge[] halfEdges = new HalfEdge[triangles.Length];
            
            // Optimizing twin search by caching edge keys
            Dictionary<EdgeKey, HalfEdge> edgeDict = new();
            
            // Initialize Half-Edges
            for (var tri = 0; tri < triangles.Length; tri += 3)
            {
                HalfEdge edge1 = new() { m_vertex = triangles[tri], m_edgeIndex = tri };
                HalfEdge edge2 = new() { m_vertex = triangles[tri + 1], m_edgeIndex = tri + 1 };
                HalfEdge edge3 = new() { m_vertex = triangles[tri + 2], m_edgeIndex = tri + 2 };
                
                edge1.m_next = edge2;
                edge2.m_next = edge3;
                edge3.m_next = edge1;
                
                edge1.m_prev = edge3;
                edge2.m_prev = edge1;
                edge3.m_prev = edge2;

                halfEdges[tri] = (edge1);
                halfEdges[tri + 1] = (edge2);
                halfEdges[tri + 2] = (edge3);
                
                // Add edges to dictionary
                EdgeKey edgeKey1 = new EdgeKey(edge1.Vertex, edge2.Vertex);
                EdgeKey edgeKey2 = new EdgeKey(edge2.Vertex, edge3.Vertex);
                EdgeKey edgeKey3 = new EdgeKey(edge3.Vertex, edge1.Vertex);
                
                // Check if the edge already exists in the dictionary
                if (edgeDict.TryGetValue(edgeKey1, out var existingEdge1))
                {
                    edge1.m_twin = existingEdge1;
                    existingEdge1.m_twin = edge1;
                }
                else
                {
                    edgeDict[edgeKey1] = edge1;
                }
                if (edgeDict.TryGetValue(edgeKey2, out var existingEdge2))
                {
                    edge2.m_twin = existingEdge2;
                    existingEdge2.m_twin = edge2;
                }
                else
                {
                    edgeDict[edgeKey2] = edge2;
                }
                if (edgeDict.TryGetValue(edgeKey3, out var existingEdge3))
                {
                    edge3.m_twin = existingEdge3;
                    existingEdge3.m_twin = edge3;
                }
                else
                {
                    edgeDict[edgeKey3] = edge3;
                }
            }
            
            return halfEdges;
        }

        public static HalfEdge[] BakeHalfEdges(Face[] faces)
        {
            List<HalfEdge> halfEdges = new ();
            
            // Optimizing twin search by caching edge keys
            Dictionary<EdgeKey, HalfEdge> edgeDict = new();
            
            // Initialize half-edges

            int edgeIndex = 0;
            for (int f = 0; f < faces.Length; f++)
            {
                // Create the half-edges for this face
                var face = faces[f];
                List<HalfEdge> faceHalfEdges = new();
                for (int e = 0; e < face.m_edgeCount; e++)
                {
                    faceHalfEdges.Add(new HalfEdge()
                    {
                        m_vertex = face.m_vertexIndices[e], 
                        m_edgeIndex = edgeIndex,
                        m_face = f
                    });
                    edgeIndex++;
                }
                
                // Link next & prev pointers, assuming face-data lays out vertices sequentially in correct winding order
                for (int e = 0; e < face.m_edgeCount; e++)
                {
                    faceHalfEdges[e].m_next = faceHalfEdges[(e + 1) % face.m_edgeCount];
                    faceHalfEdges[e].m_next.m_prev = faceHalfEdges[e];
                    
                    EdgeKey edge = new EdgeKey(faceHalfEdges[e].Vertex, faceHalfEdges[e].m_next.Vertex);
                    if (edgeDict.TryGetValue(edge, out var existingEdge))
                    {
                        faceHalfEdges[e].m_twin = existingEdge;
                        existingEdge.m_twin = faceHalfEdges[e];
                    }
                    else
                        edgeDict[edge] = faceHalfEdges[e];
                }
                
                halfEdges.AddRange(faceHalfEdges);
            }

            for (int he = 0; he < halfEdges.Count; he++)
            {
                halfEdges[he].m_edgeIndex = he;
            }
            
            // Add edges to dictionary and find twins
            //foreach (var edge in halfEdges)
            //{
            //    EdgeKey edgeKey = new EdgeKey(edge.Vertex, edge.Next.Vertex);
            //    if (edgeDict.TryGetValue(edgeKey, out var existingEdge))
            //    {
            //        edge.m_twin = existingEdge;
            //        existingEdge.m_twin = edge;
            //    }
            //    else
            //    {
            //        edgeDict[edgeKey] = edge;
            //    }
            //}
            
            return halfEdges.ToArray();
        }

        /// <summary>
        /// Given a half-edge representation of a mesh, generates a list of all edges that are boundary edges.
        /// Determined by whether a half-edge has a twin or not.
        /// </summary>
        public static HalfEdge[] BuildManifoldEdges(HalfEdge[] halfEdges)
        {
            // Collect all half-edges who do not have a twin (e.g edges belonging to only 1 triangle)
            return halfEdges.Where(edge => edge.m_twin == null).ToArray();
        }

        public HalfEdge GetAdjacentBoundaryEdge(bool reverse = false)
        {
            var adjBoundary = reverse ? Prev : Next;
            while (!adjBoundary.IsBoundary) adjBoundary = reverse ? adjBoundary.Twin.Prev : adjBoundary.Twin.Next;
            return adjBoundary;
        }

        public static List<List<HalfEdge>> BuildBoundarySmoothingGroups(Vector3[] vertices, HalfEdge[] halfEdges, float angleThreshold = 45f)
        {
            List<List<HalfEdge>> smoothingGroups = new() { new() };

            var boundaryEdges = BuildManifoldEdges(halfEdges);
            if (boundaryEdges.Length == 0) return null; 
            
            var firstEdge = boundaryEdges[0];
            var currentEdge = firstEdge;

            do
            {
                var nextBoundary = currentEdge.GetAdjacentBoundaryEdge();
                var nextNextBoundary = nextBoundary.GetAdjacentBoundaryEdge();

                // Might've added it in previous iteration
                if (!smoothingGroups[^1].Contains(currentEdge)) smoothingGroups[^1].Add(currentEdge);
                smoothingGroups[^1].Add(nextBoundary);
                
                var p1 = vertices[currentEdge.Vertex];
                var p2 = vertices[nextBoundary.Vertex];
                var p3 = vertices[nextNextBoundary.Vertex];
                
                var angle = Vector3.Angle(p2 - p1, p3 - p2);
                if (angle > angleThreshold)
                {
                    smoothingGroups.Add(new());
                }
                //else smoothingGroups[^1].Add(nextBoundary);
                
                currentEdge = nextBoundary;

            } while (currentEdge != firstEdge);
            
            // Check if first and last smoothing groups can be combined
            // (this can happen if we start in the middle of a smoothing group)
            if (smoothingGroups.Count > 2)
            {
                var firstGroup = smoothingGroups[0];
                var lastGroup = smoothingGroups[^1];
                if (firstGroup.Count > 0 && lastGroup.Count > 0)
                {
                    var loopStartEdge = firstGroup.First();
                    var loopEndEdge = lastGroup.Last();

                    if (loopStartEdge != loopEndEdge)
                    {
                        Debug.LogError($"LoopStart:\n{loopStartEdge}\nLoopEnd:\n{loopEndEdge}\nStartAdj\n{loopStartEdge.GetAdjacentBoundaryEdge()}\nEndAdj\n{loopEndEdge.GetAdjacentBoundaryEdge()}\n Boundary loop inconsistency detected");
                        return null;
                    }
                    
                    var p1 = vertices[loopEndEdge.Vertex];
                    var p2 = vertices[loopStartEdge.Vertex];
                    var p3 = vertices[loopStartEdge.GetAdjacentBoundaryEdge().Vertex];
                    
                    var angle = Vector3.Angle(p2 - p1, p3 - p2);
                    if (angle < angleThreshold)
                    {
                        // First element matches, so remove it first cus we're adding it in again
                        smoothingGroups[^1].RemoveAt(smoothingGroups[^1].Count-1);
                        smoothingGroups[^1].AddRange(smoothingGroups[0]);
                        smoothingGroups.RemoveAt(0);
                    }
                }
            }

            smoothingGroups.RemoveAll(x => x.Count == 0);
            
            return smoothingGroups;
        }

        public override string ToString()
        {
            return $"Vertex: {m_vertex}\n Next: {m_next?.m_vertex}\n Twin: {m_twin?.m_vertex}";
        }

        public bool Equals(HalfEdge other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return m_vertex == other.m_vertex && m_face == other.m_face && Equals(m_next, other.m_next) && Equals(m_prev, other.m_prev);
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((HalfEdge)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(m_vertex, m_face, m_next.m_vertex, m_prev.m_vertex);
        }
    }
}
