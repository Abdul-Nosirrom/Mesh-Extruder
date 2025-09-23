using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Drawing;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace FS.MeshProcessing
{
    [Serializable]
    public struct Face
    {
        public int[] m_vertexIndices;
        
        public int m_edgeCount => m_vertexIndices?.Length ?? 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    [Serializable]
    public struct Vertex : IEquatable<Vertex>
    {
        public float3 m_position;
        public float3 m_normal;
        public int m_numFaces;
        
        public bool Equals(Vertex other)
        {
            return m_position.Equals(other.m_position) && m_normal.Equals(other.m_normal) && m_numFaces == other.m_numFaces;
        }
    }

    [Serializable]
    public class PreservedMesh
    {
        public string m_name;
        public Face[] m_faces;
        public Vertex[] m_vertices;
        public HalfEdge[] m_halfEdges;
        
        // Stats for debugging
        public int m_quadCount;
        public int m_triangleCount;
        public int m_nGonCount;
        
        // Draw buffers
        // These are all in world-space
        [StructLayout(LayoutKind.Sequential)]
        public struct EdgeData
        {
            public float3 start;
            public float3 end;
            public float3 normal;
            public float3 right;
            public float4 color;
        }
        
        public void ConfirmMesh() => m_halfEdges = HalfEdge.BakeHalfEdges(m_faces);
        
        public void GetHalfEdgeDrawAttributes(HalfEdge edge, out float3 start, out float3 end, out float3 normal, out float3 right)
        {
            // We want the edge just veryyyy slightly offset by the face normal & inset into the face to better differentiate a half edge & its twin
            var currentVertex = m_vertices[edge.Vertex];
            var nextVertex = m_vertices[edge.Next.Vertex];
            
            start = currentVertex.m_position;
            end = nextVertex.m_position;
            normal = currentVertex.m_normal;
            
            DrawAttributes(ref start, ref end, ref normal, out right);
        }

        public static void DrawAttributes(ref float3 start, ref float3 end, ref float3 normal, out float3 right)
        {
            // Offset vector is basically an 'inset' for the half-edge line
            var edgeDir = math.normalize(end - start);
            float edgeLength = math.distance(start, end);
            
            var normalOffset = normal * 0.001f;

            start = start + edgeDir * edgeLength * 0.05f; // shorten the lengths a bit for the half-edge arrow head
            end = end + -edgeDir * edgeLength * 0.05f;

            // Normal offset for selection visibility
            start += normalOffset;
            end += normalOffset;
            
            // Inset offset for half-edge differentiation
            var insetDir = math.normalize(math.cross(normal, edgeDir));
            var offsetVector = insetDir * 0.01f;

            start -= offsetVector;
            end -= offsetVector;

            right = insetDir;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeHalfEdge : IEquatable<NativeHalfEdge>
    {
        public int Vertex;
        public int Face;

        public int EdgeIndex;
        public int Next;
        public int Prev;
        public int Twin;
        
        public bool IsBoundary => Twin == -1;
        
        public static NativeHalfEdge FromHalfEdge(HalfEdge he, HalfEdge[] halfEdges) => new NativeHalfEdge
        {
            Vertex = he.Vertex,
            Face = he.Face,
            EdgeIndex = Array.FindIndex(halfEdges, e => e.Equals(he)),
            Next = Array.FindIndex(halfEdges, e => e.Equals(he.Next)),
            Prev = Array.FindIndex(halfEdges, e => e.Equals(he.Prev)),
            Twin = he.IsBoundary ? -1 : Array.FindIndex(halfEdges, e => e.Equals(he.Twin)),
        };
        
        public NativeHalfEdge NextEdge(NativeHalfEdge[] halfEdges) => halfEdges[Next];
        public NativeHalfEdge PrevEdge(NativeHalfEdge[] halfEdges) => halfEdges[Prev];
        public NativeHalfEdge TwinEdge(NativeHalfEdge[] halfEdges) => halfEdges[Twin];
        
        // Override == operator
        public static bool operator ==(NativeHalfEdge a, NativeHalfEdge b)
        {
            return a.Vertex == b.Vertex && a.Face == b.Face && a.Next == b.Next && a.Prev == b.Prev && a.Twin == b.Twin;
        }
        
        // Override != operator
        public static bool operator !=(NativeHalfEdge a, NativeHalfEdge b)
        {
            return !(a == b);
        }

        public void GetHalfEdgeDrawAttributes(NativeMesh mesh, float4x4 localToWorld, out float3 start, out float3 end, out float3 normal, out float3 right)
        {
            // We want the edge just veryyyy slightly offset by the face normal & inset into the face to better differentiate a half edge & its twin
            var currentVertex = mesh.m_vertices[Vertex];
            var nextVertex = mesh.m_vertices[mesh.m_halfEdges[Next].Vertex];
            
            start = currentVertex.m_position;
            end = nextVertex.m_position;
            normal = currentVertex.m_normal;
            
            PreservedMesh.DrawAttributes(ref start, ref end, ref normal, out right);
 

            start = math.mul(localToWorld, new float4(start, 1f)).xyz;
            end = math.mul(localToWorld, new float4(end, 1f)).xyz;
        }

        public bool Equals(NativeHalfEdge other)
        {
            return Vertex == other.Vertex && Face == other.Face && Next == other.Next && Prev == other.Prev && Twin == other.Twin;
        }

        public override string ToString()
        {
            return $"V:{Vertex}\nF:{Face}\nN:{Next}\nP:{Prev}\nT:{Twin}";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeMesh
    {
        public NativeArray<Vertex> m_vertices;
        public NativeArray<NativeHalfEdge> m_halfEdges;
        
        public bool Initialized { get; private set; }
        
        public static implicit operator NativeMesh(PreservedMesh mesh)
        {
            var burstMesh = new NativeMesh();
            
            burstMesh.m_vertices = new NativeArray<Vertex>(mesh.m_vertices.Length, Allocator.Persistent);
            for (int i = 0; i < mesh.m_vertices.Length; i++)
            {
                burstMesh.m_vertices[i] = mesh.m_vertices[i];
            }
            
            burstMesh.m_halfEdges = new NativeArray<NativeHalfEdge>(mesh.m_halfEdges.Length, Allocator.Persistent);
            for (int i = 0; i < mesh.m_halfEdges.Length; i++)
            {
                burstMesh.m_halfEdges[i] = NativeHalfEdge.FromHalfEdge(mesh.m_halfEdges[i], mesh.m_halfEdges);
            }
            
            burstMesh.Initialized = true;
            
            return burstMesh;
        }

        public void Dispose()
        {
            Initialized = false;
            
            if (m_vertices.IsCreated)
                m_vertices.Dispose();
            if (m_halfEdges.IsCreated)
                m_halfEdges.Dispose();
        }
    }
    
    public class MeshTopologyPreserver : MonoBehaviourGizmos
    {
        [SerializeField, HideInInspector] private PreservedMesh m_preservedMesh;
        public PreservedMesh PreservedMesh => m_preservedMesh;

        public void SetMesh(PreservedMesh mesh)
        {
            m_preservedMesh = mesh;
            m_preservedMesh.ConfirmMesh();
        }

        private void Awake()
        {
            Destroy(this); // We only need this component at edit-time TODO: Remove it during build-time
        }
    }
}