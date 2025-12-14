using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.ProBuilder;

namespace FS.MeshProcessing
{
    public static class MeshQuadify
    {
        public static void Quadify(this PreservedMesh mesh, bool smoothing = true)
        {
            HashSet<int> processedFaces = new HashSet<int>();
            List<HalfEdge> halfEdges = (mesh.m_halfEdges ?? HalfEdge.BakeHalfEdges(mesh.m_faces)).ToList();
            
            // Build edge connection scores
            Dictionary<EdgeKey, float> connections = new Dictionary<EdgeKey, float>();
            
            foreach (var halfEdge in halfEdges)
            {
                if (!halfEdge.IsBoundary)
                {
                    var edgeLookup = new EdgeKey(halfEdge.Vertex, halfEdge.Next.Vertex);
                    if (!connections.ContainsKey(edgeLookup))
                    {
                        float score = mesh.GetQuadScore(halfEdge, halfEdge.Twin);
                        connections.Add(edgeLookup, score);
                    }
                }
            }
            
            List<SimpleTuple<int, int>> quads = new();
            
            // Find best quad pairs
            foreach (var halfEdge in halfEdges)
            {
                if (!processedFaces.Add(halfEdge.Face)) continue;
                
                float bestScore = 0f;
                int buddyFace = -1;
                HalfEdge bestEdge = null;
                
                var currentEdge = halfEdge;
                do
                {
                    if (!currentEdge.IsBoundary && !processedFaces.Contains(currentEdge.Twin.Face))
                    {
                        var edgeLookup = new EdgeKey(currentEdge.Vertex, currentEdge.Next.Vertex);
                        if (connections.TryGetValue(edgeLookup, out float score) && score > bestScore)
                        {
                            // Check if this pairing is mutual (both faces prefer each other)
                            int reciprocalBest = GetBestQuadConnection(currentEdge.Twin, connections);
                            if (reciprocalBest == currentEdge.Face)
                            {
                                bestScore = score;
                                buddyFace = currentEdge.Twin.Face;
                                bestEdge = currentEdge;
                            }
                        }
                    }
                    currentEdge = currentEdge.Next;
                } while (!currentEdge.Equals(halfEdge));
                
                if (buddyFace >= 0 && bestEdge != null)  // Fixed: >= 0 instead of > 0
                {
                    processedFaces.Add(buddyFace);
                    // Store the shared edge information for proper merging
                    quads.Add(new SimpleTuple<int, int>(halfEdge.Face, buddyFace));
                }
            }
            
            MergeQuadPairs(mesh, quads, halfEdges, smoothing);
        }

        private static void MergeQuadPairs(PreservedMesh mesh, List<SimpleTuple<int, int>> pairs, 
            List<HalfEdge> halfEdges, bool collapseCoincidentVertices = true)
        {
            HashSet<int> remove = new();
            List<Face> add = new();
            
            foreach (var pair in pairs)
            {
                var leftFace = mesh.m_faces[pair.item1];
                var rightFace = mesh.m_faces[pair.item2];
                
                // Find the shared edge between the two faces
                HalfEdge sharedEdge = null;
                foreach (var he in halfEdges)
                {
                    if (he.Face == pair.item1 && !he.IsBoundary && he.Twin.Face == pair.item2)
                    {
                        sharedEdge = he;
                        break;
                    }
                }
                
                if (sharedEdge == null) continue;
                
                // Build the quad properly by removing shared vertices
                int[] quad = MakeQuad(sharedEdge, sharedEdge.Twin);
                if (quad != null && quad.Length == 4)
                {
                    add.Add(new Face() { m_vertexIndices = quad });
                    remove.Add(pair.item1);
                    remove.Add(pair.item2);
                }
            }
            
            // Rebuild face list
            List<Face> faces = new();
            for (int i = 0; i < mesh.m_faces.Length; i++)
            {
                if (!remove.Contains(i))
                    faces.Add(mesh.m_faces[i]);
            }
            faces.AddRange(add);
            
            mesh.m_faces = faces.ToArray();
            mesh.ConfirmMesh();
            
            // Update statistics
            mesh.m_triangleCount = 0;
            mesh.m_quadCount = 0;
            mesh.m_nGonCount = 0;
            
            foreach (var face in mesh.m_faces)
            {
                if (face.m_edgeCount == 3) mesh.m_triangleCount++;
                else if (face.m_edgeCount == 4) mesh.m_quadCount++;
                else mesh.m_nGonCount++;
            }
        }


        /// <summary>
        /// Get a weighted value for the quality of a quad composed of two triangles. 0 is terrible, 1 is perfect.
        /// Normal threshold will discard any quads where the dot product of their normals is less than the threshold
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="heA"></param>
        /// <param name="heB"></param>
        /// <returns></returns>
        public static float GetQuadScore(this PreservedMesh mesh, HalfEdge left, HalfEdge right, float normalThreshold = .9f)
        {
            var vertices = mesh.m_vertices;

            int[] quad = MakeQuad(left, right);
            if (quad == null)
                return 0f;
            
            // Check normals
            var leftNormal = mesh.FaceNormal(left);
            var rightNormal = mesh.FaceNormal(right);
            
            float score = Vector3.Dot(leftNormal, rightNormal);
            
            if (score < normalThreshold)
                return 0f;
            
            // Next is right-angle-ness check
            Vector3 a = ((Vector3)(vertices[quad[1]].m_position - vertices[quad[0]].m_position)).normalized;
            Vector3 b = ((Vector3)(vertices[quad[2]].m_position - vertices[quad[1]].m_position)).normalized;
            Vector3 c = ((Vector3)(vertices[quad[3]].m_position - vertices[quad[2]].m_position)).normalized;
            Vector3 d = ((Vector3)(vertices[quad[0]].m_position - vertices[quad[3]].m_position)).normalized;
            
            float da = Mathf.Abs(Vector3.Dot(a, b));
            float db = Mathf.Abs(Vector3.Dot(b, c));
            float dc = Mathf.Abs(Vector3.Dot(c, d));
            float dd = Mathf.Abs(Vector3.Dot(d, a));

            score += 1f - ((da + db + dc + dd) * .25f);

            // and how close to parallel the opposite sides area
            score += Mathf.Abs(Vector3.Dot(a, c)) * .5f;
            score += Mathf.Abs(Vector3.Dot(b, d)) * .5f;

            // the three tests each contribute 1
            return score * .33f;
        }


        public static int FaceEdgeCount(this HalfEdge he)
        {
            int count = 0;
            var startEdge = he;
            var currentEdge = startEdge;
            do
            {
                count++;
                currentEdge = currentEdge.Next;
            } while (!currentEdge.Equals(startEdge));
            return count;
        }
        
        /// <summary>
        /// Given two-half edges that are twins, make a quad from their vertices
        /// </summary>
        public static int[] MakeQuad(HalfEdge left, HalfEdge right)
        {
            if (left.FaceEdgeCount() != 3 || right.FaceEdgeCount() != 3)
                return null;
            
            int[] quad = new int[4];
            quad[0] = left.Vertex;
            quad[1] = right.Next.Next.Vertex;
            quad[2] = right.Vertex;
            quad[3] = left.Next.Next.Vertex;
            return quad;
        }
        
        public static Vector3 FaceNormal(this PreservedMesh mesh, HalfEdge he)
        {
            var v0 = mesh.m_vertices[he.Vertex].m_position;
            var v1 = mesh.m_vertices[he.Next.Vertex].m_position;
            var v2 = mesh.m_vertices[he.Next.Next.Vertex].m_position;

            return Vector3.Cross(v1 - v0, v2 - v0).normalized;
        }
        
        public static int GetBestQuadConnection(HalfEdge he, Dictionary<EdgeKey, float> connections)
        {
            float bestScore = 0f;
            int bestFace = -1;
    
            var currentEdge = he;
            do
            {
                if (!currentEdge.IsBoundary)
                {
                    var edge = new EdgeKey(currentEdge.Vertex, currentEdge.Next.Vertex);
                    if (connections.TryGetValue(edge, out float score) && score > bestScore)
                    {
                        bestScore = score;
                        bestFace = currentEdge.Twin.Face;
                    }
                }
                currentEdge = currentEdge.Next;
            } while (!currentEdge.Equals(he));
    
            return bestFace;
        }
    }
}