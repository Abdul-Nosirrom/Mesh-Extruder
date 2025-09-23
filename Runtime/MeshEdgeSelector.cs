using System;
using System.Collections.Generic;
using System.Linq;
using Drawing;
using UnityEngine;
using UnityEngine.Splines;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FS.MeshProcessing
{
    [RequireComponent(typeof(MeshVertexPath))]
    public class MeshEdgeSelector : MonoBehaviourGizmos
    {
        [SerializeField, Range(-180f, 180f)] private float m_normalRotationOffset = 0f;
        [SerializeField, Range(0, 180f)] private float m_edgeLoopAngleShiftLimit = 90f;
        [SerializeField] private int[] m_selectedEdges = Array.Empty<int>();
        [SerializeField] private MeshTopologyPreserver m_mesh;
        
        public int[] SelectedEdges => m_selectedEdges;

        private void Awake()
        {
            Destroy(this); // TODO: Remove during build-time also, this is for editor purposes only
        }

        public void SelectEdgeLoopFromSelection()
        {
            if (m_selectedEdges.Length is < 1 or > 2) return;
            
            var mesh = m_mesh.PreservedMesh;

            var startEdge = mesh.m_halfEdges[m_selectedEdges[0]];
            var endEdge = mesh.m_halfEdges[m_selectedEdges[^1]];
            if (startEdge.Equals(endEdge)) endEdge = null;
            
            // Walk forward & backward from the selected edges to find a loop. Stopping at EndEdge if that exists
            if (startEdge.IsBoundary)
                WalkBoundaryEdgeLoop(startEdge, endEdge);
            else 
                WalkRegularEdgeLoop(startEdge, endEdge);
        }

        private void WalkBoundaryEdgeLoop(HalfEdge startEdge, HalfEdge endEdge)
        {
            var edgeLoop = new HashSet<int>();
            var mesh = m_mesh.PreservedMesh;

            // Walk forward
            var currentEdge = startEdge;
            edgeLoop.Add(currentEdge.EdgeIndex);
            HalfEdge prevEdge = startEdge;
            while (currentEdge.IsBoundary)
            {
                // Move forward
                currentEdge = currentEdge.GetAdjacentBoundaryEdge();

                if (prevEdge != null)
                {
                    var edge1 = mesh.m_vertices[currentEdge.Vertex].m_position - mesh.m_vertices[currentEdge.Next.Vertex].m_position;
                    var edge2 = mesh.m_vertices[prevEdge.Vertex].m_position - mesh.m_vertices[prevEdge.Next.Vertex].m_position;
                    
                    var angle = Vector3.Angle(edge1, edge2);
                    if (angle >= m_edgeLoopAngleShiftLimit) break;
                }
                
                // Full loop
                if (currentEdge.EdgeIndex == startEdge.EdgeIndex) break;
                
                // Add it to list
                edgeLoop.Add(currentEdge.EdgeIndex);
                
                // Ensure we're not "done"
                if (endEdge != null && currentEdge.EdgeIndex == endEdge.EdgeIndex) break;
                
                prevEdge = currentEdge;
            }
            
            // Walk backward (ye bad code, but whatever)
            currentEdge = startEdge;
            edgeLoop.Add(currentEdge.EdgeIndex);
            prevEdge = startEdge;
            while (currentEdge.IsBoundary)
            {
                // Move forward
                currentEdge = currentEdge.GetAdjacentBoundaryEdge(true);

                if (prevEdge != null)
                {
                    var edge1 = mesh.m_vertices[currentEdge.Prev.Vertex].m_position - mesh.m_vertices[currentEdge.Vertex].m_position;
                    var edge2 = mesh.m_vertices[prevEdge.Prev.Vertex].m_position - mesh.m_vertices[prevEdge.Vertex].m_position;
                    
                    var angle = Vector3.Angle(edge1, edge2);
                    if (angle >= m_edgeLoopAngleShiftLimit) break; // we've made a sharp turn, stop the loop here
                }
                
                // Full loop
                if (currentEdge.EdgeIndex == startEdge.EdgeIndex) break;
                
                // Add it to list
                edgeLoop.Add(currentEdge.EdgeIndex);
                
                // Ensure we're not "done"
                if (endEdge != null && currentEdge.EdgeIndex == endEdge.EdgeIndex) break;
                
                prevEdge = currentEdge;
            }

            m_selectedEdges = edgeLoop.ToArray();
            SortSelectedEdges();
        }
        
        private void WalkRegularEdgeLoop(HalfEdge startEdge, HalfEdge endEdge)
        {
            // TODO: I dont think this works well, sometimes we dont hit the last end edge
            
            // Hueristic for the interior edge walk is: not a boundary, each vertex is apart of the same # faces
            // Walk is essentially edge.next.twin.next
            
            // Backwards via Prev.Twin.Prev till we either loop around or our hueristic fails
            while (true)
            {
                var prev = startEdge.Prev?.Twin?.Prev;
                if (prev == null || prev.IsBoundary) break;

                var prevNextVertex = m_mesh.PreservedMesh.m_vertices[prev.Next.Vertex];
                if (!EdgeLoopHueristic(startEdge, prev, prevNextVertex, true)) break;
                if (prev.Equals(startEdge) || prev.Equals(endEdge)) break; // full loop or reached the end
                startEdge = prev;
            }
            
            // Now iterate forwards via Next.Twin.Next
            var current = startEdge;
            HashSet<int> edgeLoop = new() { current.EdgeIndex };
            while (true)
            {
                var next = current.Next?.Twin?.Next;
                if (next == null || next.IsBoundary) break;
                
                // Evaluate topological condition
                var currentNextVertex = m_mesh.PreservedMesh.m_vertices[current.Next.Vertex];
                if (!EdgeLoopHueristic(current, next, currentNextVertex, false)) break;

                current = next;
                edgeLoop.Add(current.EdgeIndex);
                
                // Have we looped around or reached the end?
                if (next.Equals(startEdge) || next.Equals(endEdge)) break; // full loop or reached the end
            }
            
            m_selectedEdges = edgeLoop.ToArray();
            SortSelectedEdges();
        }
        
        private bool EdgeLoopHueristic(HalfEdge e1, HalfEdge e2, Vertex? additionalVertTopo = null, bool compareAgainstE1 = false)
        {
            var mesh = m_mesh.PreservedMesh;

            // Check topological condition
            var v1 = mesh.m_vertices[e1.Vertex];
            var v2 = mesh.m_vertices[e2.Vertex];

            // NOTE: Super scuffed way to solve the issue of start/end edges (depending on walk direction) having different connectivity on the actual ends
            bool faceCheckPass = false;
            if (additionalVertTopo != null)
            {
                if (compareAgainstE1) faceCheckPass = v1.m_numFaces == additionalVertTopo.Value.m_numFaces;
                else faceCheckPass = v2.m_numFaces == additionalVertTopo.Value.m_numFaces;
            }

            if (!faceCheckPass && v1.m_numFaces != v2.m_numFaces) 
            {
                Debug.LogError($"[Edge Loop] Stopped walk due to topological mismatch:" +
                               $"\n- Edge from Vertex {e1.Vertex} to {e1.Next.Vertex} has {v1.m_numFaces} faces, " +
                               $"\n- Edge from Vertex {e2.Vertex} to {e2.Next.Vertex} has {v2.m_numFaces} faces.");
                return false; // topological mismatch
            }
                
            // Check angle condition
            var edge1 = mesh.m_vertices[e1.Vertex].m_position - mesh.m_vertices[e1.Next.Vertex].m_position;
            var edge2 = mesh.m_vertices[e2.Vertex].m_position - mesh.m_vertices[e2.Next.Vertex].m_position;
            var angle = Vector3.Angle(edge1, edge2);
            if (angle >= m_edgeLoopAngleShiftLimit) 
            {
                Debug.LogError($"[Edge Loop] Stopped walk due to sharp angle change of {angle}° " +
                               $"\n- Edge from Vertex {e1.Vertex} to {e1.Next.Vertex}" +
                               $"\n- Edge from Vertex {e2.Vertex} to {e2.Next.Vertex}");
                return false; // we've made a sharp turn, stop the loop here
            }

            return true;
        }

        public void SetSplineFromSelection()
        {
            if (m_mesh == null) return;

            SortSelectedEdges();
            
            var mvp = GetComponent<MeshVertexPath>();
            
            BezierKnot[] knots = new BezierKnot[m_selectedEdges.Length + 1];
            var mesh = m_mesh.PreservedMesh;
            
            for (int knotIdx = 0; knotIdx < m_selectedEdges.Length; knotIdx++)
            {
                var edge = mesh.m_halfEdges[m_selectedEdges[knotIdx]];
                if (edge == null) continue;

                var v0 = transform.TransformPoint(mesh.m_vertices[edge.Vertex].m_position);
                var v1 = transform.TransformPoint(mesh.m_vertices[edge.Next.Vertex].m_position);

                var normal = transform.TransformDirection(mesh.m_vertices[edge.Vertex].m_normal).normalized;
                var tangent = (v1 - v0).normalized;
                normal = Quaternion.AngleAxis(m_normalRotationOffset, tangent) * normal;

                knots[knotIdx] = new BezierKnot()
                {
                    Position = v0,
                    Rotation = Quaternion.LookRotation(tangent, normal),
                };

                if (knotIdx == m_selectedEdges.Length - 1)
                {
                    // Add last vertex
                    knots[knotIdx + 1] = new BezierKnot()
                    {
                        Position = v1,
                        Rotation = Quaternion.LookRotation(tangent, normal),
                    };
                }
            }

            mvp.SetVertexPath(knots);
        }

        private void SortSelectedEdges()
        {
            EdgeKey[] selectedEdges = new EdgeKey[m_selectedEdges.Length];
            int[] sortedEdges = new int[m_selectedEdges.Length];
            Dictionary<EdgeKey, int> edgeToHalfEdgeIdx = new();
            
            for (int e = 0; e < m_selectedEdges.Length; e++)
            {
                var edgeIdx = m_selectedEdges[e];
                HalfEdge edge = m_mesh.PreservedMesh.m_halfEdges[edgeIdx];
                selectedEdges[e] = new EdgeKey(edge.Vertex, edge.Next.Vertex);
                edgeToHalfEdgeIdx[selectedEdges[e]] = edgeIdx;
            }
            
            if (selectedEdges.Length > 0)
            {
                var trackedEdges = new HashSet<EdgeKey>(selectedEdges);
                
                // Need to order it such that element ones [a] is unique, and each subsequent element [n] has its [a] equal to the previous element's [b]

                // Find the starting edge (one whose 'a' vertex is unique)
                EdgeKey currentEdge = trackedEdges.ToArray()[0];
                foreach (var edge in trackedEdges)
                {
                    bool hasForwardConnection = false;
                    foreach (var forwardEdge in trackedEdges)
                    {
                        if (forwardEdge.b == edge.a)
                        {
                            hasForwardConnection = true;
                            break;
                        }
                    }

                    if (!hasForwardConnection)
                    {
                        currentEdge = edge;
                        break;
                    }
                }

                if (currentEdge.Equals(default))
                {
                    // TODO: if closed loop, we can start anywhere, so just pick one and go
                    Debug.LogError(
                        "Could not find a unique starting edge, the selection may form a closed loop or be invalid.");
                    return;
                }

                sortedEdges[0] = edgeToHalfEdgeIdx[currentEdge];
                trackedEdges.Remove(currentEdge);
                int idx = 1;
                while (trackedEdges.Count > 0)
                {
                    var nextEdge = trackedEdges.FirstOrDefault(edge => edge.a == currentEdge.b);
                    if (nextEdge.Equals(default))
                    {
                        Debug.LogWarning("Could not find a connecting edge, the selection may be disjointed.");
                        break;
                    }

                    sortedEdges[idx] = edgeToHalfEdgeIdx[nextEdge];
                    trackedEdges.Remove(nextEdge);
                    currentEdge = nextEdge;
                    idx++;
                }

                m_selectedEdges = sortedEdges;
            }
        }

        public override void DrawGizmos()
        {
#if UNITY_EDITOR            
            if (Application.isPlaying) return;
            
            // just draw the selected edges when not selected, it already draws when selected
            if (Selection.Contains(gameObject)) return;
            
            if (m_selectedEdges.Length < 1) return;
            if (m_mesh == null || m_mesh.PreservedMesh == null) return;
            var mesh = m_mesh.PreservedMesh;
            foreach (var edgeIdx in m_selectedEdges)
            {
                var edge = mesh.m_halfEdges[edgeIdx];
                if (edge == null) continue;
                
                var v0 = transform.TransformPoint(mesh.m_vertices[edge.Vertex].m_position);
                var v1 = transform.TransformPoint(mesh.m_vertices[edge.Next.Vertex].m_position);
                
                Draw.Line(v0, v1, Color.red);
            }
#endif            
        }
    }
}