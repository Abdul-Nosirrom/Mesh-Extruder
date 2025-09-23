using System;
using System.Runtime.InteropServices;
using Drawing;
using Unity.Jobs;
using UnityEditor;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

namespace FS.MeshProcessing.Editor
{
    
    [CustomEditor(typeof(MeshEdgeSelector))]
    public class MeshEdgeSelectorEditor : UnityEditor.Editor
    {
        private MeshEdgeSelector m_target;
        private MeshTopologyPreserver m_preservedMesh;
        private NativeMesh m_nativeMesh;

        private JobHandle m_edgeVisualizerHandle;
        
        private NativeHalfEdge m_hoveredEdge;

        private SerializedProperty m_selectedEdgesProp;
        private SerializedProperty m_meshProp;
        
        private GraphicsBuffer m_edgeVizBuffer;
        private NativeArray<EdgeVisualizerAttributes.EdgeData> m_edgeData;

        private void OnEnable()
        {
            m_target = (MeshEdgeSelector)this.target;
            m_selectedEdgesProp = serializedObject.FindProperty("m_selectedEdges");
            m_meshProp = serializedObject.FindProperty("m_mesh");
            if (m_target.TryGetComponent<MeshTopologyPreserver>(out m_preservedMesh))
            {
                serializedObject.Update();
                m_meshProp.objectReferenceValue = m_preservedMesh;
                serializedObject.ApplyModifiedProperties();
                
                m_nativeMesh = m_preservedMesh.PreservedMesh;
                m_edgeVizBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_nativeMesh.m_halfEdges.Length, Marshal.SizeOf(typeof(EdgeVisualizerAttributes.EdgeData)));
                m_edgeData = new NativeArray<EdgeVisualizerAttributes.EdgeData>(m_nativeMesh.m_halfEdges.Length, Allocator.Persistent);
                UpdateBuffers();
            }
        }
        
        private void OnDisable()
        {
            m_nativeMesh.Dispose();
            m_edgeVizBuffer?.Dispose();
            m_edgeData.Dispose();

            m_edgeVizBuffer = null;
        }

        public override void OnInspectorGUI()
        {
            if (!m_nativeMesh.Initialized)
            {
                EditorGUILayout.HelpBox("Ensure the imported mesh had its topology preserved!", MessageType.Error);
                if (m_meshProp.objectReferenceValue == null && m_target.TryGetComponent<MeshTopologyPreserver>(out m_preservedMesh))
                {
                    serializedObject.Update();
                    m_meshProp.objectReferenceValue = m_preservedMesh;
                    serializedObject.ApplyModifiedProperties();
                    
                    m_nativeMesh = m_preservedMesh.PreservedMesh;
                }
            }
            else
            {
                GUI.enabled = m_target.TryGetComponent<MeshVertexPath>(out var vertexPath) && m_selectedEdgesProp.arraySize >= 1;
                if (GUILayout.Button("Set Spline To Selected Edges"))
                {
                    SetSpline();
                }
                GUI.enabled = true;

                GUI.enabled = m_selectedEdgesProp.arraySize is >= 1 and <= 2;
                if (GUILayout.Button("Select Edge Loop"))
                {
                    SelectEdgeLoop();
                }
                GUI.enabled = true;
                
                if (GUILayout.Button("Clear Selection"))
                {
                    ClearSelection();
                }
                
                base.OnInspectorGUI();
            }
        }

        private void SetSpline()
        {
            if (!m_target.TryGetComponent<MeshVertexPath>(out var vertexPath) || m_selectedEdgesProp.arraySize < 1) return;
            Undo.RecordObject(vertexPath, "Set Spline From Selected Edges");
            m_target.SetSplineFromSelection();
            EditorUtility.SetDirty(vertexPath);
            UpdateBuffers();
        }
        
        private void SelectEdgeLoop()
        {
            if (m_selectedEdgesProp.arraySize is >= 1 and <= 2)
            {
                Undo.RecordObject(m_target, "Set Spline From Selected Edges");
                m_target.SelectEdgeLoopFromSelection();
                EditorUtility.SetDirty(m_target);
                UpdateBuffers();
            }
        }

        private void ClearSelection()
        {
            serializedObject.Update();
            m_selectedEdgesProp.arraySize = 0;
            serializedObject.ApplyModifiedProperties();
            UpdateBuffers();
        }

        private void OnSceneGUI()
        {
            //if (!m_selectionEnabled) return;
            if (!m_nativeMesh.Initialized || m_nativeMesh.m_halfEdges.Length == 0) return;
        
            //if (Event.current.type == EventType.Repaint)
                VisualizeEdges();
            if (Event.current.type == EventType.MouseMove)
            {
                Profiler.BeginSample("[Edge Selector] Edge Hover Selection IDs");
                // Read
                var id = WireframeDrawing.ReadSelectionID(Event.current.mousePosition);
                var prevID = m_hoveredEdge.EdgeIndex;
                
                if (id >= 0 && id < m_nativeMesh.m_halfEdges.Length)
                {
                    m_hoveredEdge = m_nativeMesh.m_halfEdges[id];
                }
                else
                {
                    m_hoveredEdge = default;
                    m_hoveredEdge.EdgeIndex = -1;
                }
                
                if (prevID != id)
                    UpdateBuffers();
                Profiler.EndSample();
            }
            
            if (Event.current.type == EventType.MouseDown && m_hoveredEdge.EdgeIndex >= 0)
                HandleSelection(m_hoveredEdge.EdgeIndex);
            
            var currentEvt = Event.current;
            // If we press control + G, select spline
            if (currentEvt != null && currentEvt.type == EventType.KeyDown && currentEvt.keyCode == KeyCode.G && currentEvt.control)
            {
                SetSpline();
                currentEvt.Use();
            }
        }

        private void HandleSelection(int hoveredEdgeIdx)
        {
            if (!(Event.current.type == EventType.MouseDown && Event.current.button == 0)) return;
            
            serializedObject.Update();
            
            m_selectedEdgesProp ??= serializedObject.FindProperty("m_selectedEdges");
            
            // Shift click add, control click remove, regular click select only
            if (Event.current.shift)
            {
                m_selectedEdgesProp.arraySize++;
                m_selectedEdgesProp.GetArrayElementAtIndex(m_selectedEdgesProp.arraySize - 1).intValue = hoveredEdgeIdx;
            }
            else if (Event.current.control)
            {
                for (int i = 0; i < m_selectedEdgesProp.arraySize; i++)
                {
                    if (m_selectedEdgesProp.GetArrayElementAtIndex(i).intValue == hoveredEdgeIdx)
                    {
                        m_selectedEdgesProp.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }
            }
            else
            {
                if (m_selectedEdgesProp.arraySize == 1 && m_selectedEdgesProp.GetArrayElementAtIndex(0).intValue == hoveredEdgeIdx)
                {
                    SelectEdgeLoop();
                }
                else
                {
                    m_selectedEdgesProp.arraySize = 1;
                    m_selectedEdgesProp.GetArrayElementAtIndex(0).intValue = hoveredEdgeIdx;
                }
            }
            
            serializedObject.ApplyModifiedProperties();
            Event.current.Use();
        }

        private void VisualizeEdges()
        {
            if (m_preservedMesh == null && !m_target.TryGetComponent(out m_preservedMesh)) return;
            WireframeDrawing.DrawWireframe(m_edgeVizBuffer, m_target.transform.localToWorldMatrix);
        }

        private void UpdateBuffers()
        {
            Profiler.BeginSample("[Edge Selector] Updating Instance Buffers");
            var edgeVizAttrJob = new EdgeVisualizerAttributes
            {
                m_edgeData = m_edgeData,
                m_hovered = m_hoveredEdge,
                m_selected = new NativeArray<int>(m_target.SelectedEdges, Allocator.TempJob),
                m_mesh = m_nativeMesh
            };
            
            m_edgeVisualizerHandle = edgeVizAttrJob.Schedule(m_nativeMesh.m_halfEdges.Length, 64);
            
            m_edgeVisualizerHandle.Complete();
            
            m_edgeVizBuffer.SetData(m_edgeData);
            edgeVizAttrJob.m_selected.Dispose();
            Profiler.EndSample();
        }
    }
}