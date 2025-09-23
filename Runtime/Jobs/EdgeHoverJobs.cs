using System.Runtime.InteropServices;
using Drawing;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace FS.MeshProcessing
{
    [BurstCompile]
    public struct EdgeSelector : IJob
    {
        [ReadOnly] public NativeMesh m_mesh;
        [ReadOnly] public float4x4 m_transform;
        [ReadOnly] public float2 cursorPos;
        [ReadOnly] public float4x4 m_worldToScreen;
        [ReadOnly] public float2 screenSize;
        
        [ReadOnly] public float3 cameraForward;

        [WriteOnly] public NativeArray<int> hoveredIndex;
        [WriteOnly] public NativeArray<bool> isHovering;
        
        private float hoveredDistance;

        public void Execute()
        {
            hoveredDistance = float.MaxValue;
            isHovering[0] = false;
            hoveredIndex[0] = -1;
            
            for (int i = 0; i < m_mesh.m_halfEdges.Length; i++)
                Execute(i);
        }
        
        public void Execute(int index)
        {
            var halfEdge = m_mesh.m_halfEdges[index];
            
            var currentVertex = m_mesh.m_vertices[halfEdge.Vertex];
            var n0 = currentVertex.m_normal;
            var n0WS = math.normalize(math.mul((float3x3)m_transform, n0));
            if (math.dot(n0WS, cameraForward) > 0f) return; // Back-facing
            
            halfEdge.GetHalfEdgeDrawAttributes(m_mesh, m_transform, out var start, out var end, out _, out _);
            bool isHoveringLocal = IsHovering(start, end, out var distToLine);

            if (isHoveringLocal && distToLine < hoveredDistance)
            {
                isHovering[0] = true;
                hoveredDistance = distToLine;
                hoveredIndex[0] = index;
            }
        }
        
        private bool IsHovering(float3 p0, float3 p1, out float distToLine)
        {
            // Convert p0 & p1 to screen space
            var p0s = WorldToScreen(p0);
            var p1s = WorldToScreen(p1);
    
            var lineDir = math.normalize((p1s - p0s).xy);  
            var toCursor = cursorPos - p0s.xy;
            var proj = math.dot(toCursor, lineDir);
            var closestPoint = p0s.xy + lineDir * proj;
            distToLine = math.length(closestPoint - cursorPos);
            return distToLine < 10f && proj > 0 && proj < math.length(p1s.xy - p0s.xy);
        }
        
        private float2 WorldToScreen(float3 worldPos)
        {
            var pos = math.mul(m_worldToScreen, new float4(worldPos, 1f));
            pos /= pos.w;
            float2 ndc = pos.xy; // Normalized Device Coordinates
            
            // Convert NDC to screen space pixels
            //ndc.y = -ndc.y; // Invert Y for screen space
            ndc = (ndc + 1f) * 0.5f; // Map from [-1, 1] to [0, 1]
            ndc *= screenSize; // Scale to screen size
            
            return ndc;
        }
    }

    [BurstCompile]
    public struct EdgeVisualizerAttributes : IJobParallelFor
    {
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

        [WriteOnly] public NativeArray<EdgeData> m_edgeData;
        [ReadOnly] public NativeArray<int> m_selected;
        [ReadOnly] public NativeHalfEdge m_hovered;
        [ReadOnly] public NativeMesh m_mesh;

        public void Execute(int index)
        {
            var halfEdge = m_mesh.m_halfEdges[index];
            halfEdge.GetHalfEdgeDrawAttributes(m_mesh, float4x4.identity, out var start, out var end, out var normal, out var right);

            bool isHovered = halfEdge.EdgeIndex == m_hovered.EdgeIndex;
            bool isSelected = m_selected.IsCreated && m_selected.Contains(halfEdge.EdgeIndex);

            float4 color = halfEdge.IsBoundary ? (Vector4)Color.darkBlue : (Vector4)Color.black;
            if (isSelected) color = (Vector4)Color.yellow;
            if (isHovered) color = (Vector4)Color.cyan;
            
            EdgeData edge = new EdgeData()
            {
                start = start,
                end = end,
                normal = normal,
                right = right,
                color = color
            };
            
            m_edgeData[index] = edge;
        }
    }
}