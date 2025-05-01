using System;
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.ProBuilder;
using UnityEngine;
using UnityEngine.ProBuilder;
using HandleUtility = UnityEditor.HandleUtility;
using Math = System.Math;
using Random = UnityEngine.Random;

namespace FS.MeshProcessing
{
    [CreateAssetMenu(fileName = "MeshProfileConfig", menuName = "FS/Mesh Profile Config")]
    public class MeshProfileConfig : ScriptableObject
    {
        [SerializeField, PreviewField] 
        private Mesh m_mesh;
        
        [SerializeField, Range(0, 90)]
        public float m_smoothingAngle = 45f;
        
        [SerializeField, PreviewField, NonReorderable]
        public Material[] m_defaultMaterials;
        
        [HideInInspector] 
        public int m_vertexPathIdx = 0;

        [HideInInspector] 
        public int m_groundSnappingPivotIdx = 0;
        
        [HideInInspector]
        public Quaternion m_rotation = Quaternion.identity;
        
        public Mesh ProfileMesh => m_mesh;

        
        /// <summary>
        /// Invoked with the profile config that was modified, and a boolean indicating whether the mesh associated with it
        /// was changed
        /// </summary>
        public static Action<MeshProfileConfig> OnModified;
        
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(MeshProfileConfig))]
    public class MeshExtrusionProfileEditor : UnityEditor.Editor
    {
        private MeshProfileConfig m_config;
        private PropertyTree m_propertyTree;


        private PreviewRenderUtility m_previewRender;

        private Material m_wireFrameMat;
        private float m_camZoomFactor = -2f;

        private SerializedProperty m_serializedVertexPathIdx;
        private SerializedProperty m_serializedMaterialArray;
        private SerializedProperty m_serializedGroundSnappingPivotIdx;
        private SerializedProperty m_serializedRotation;

        private Rect m_previewRect;
        
        private List<List<HalfEdge>> m_boundarySmoothingGroups;
        
        private void OnEnable()
        {
            m_config = target as MeshProfileConfig;

            if (m_previewRender != null)
                m_previewRender.Cleanup();
            
            m_previewRender = new PreviewRenderUtility();
            m_previewRender.camera.transform.position = new Vector3(0, 0, m_camZoomFactor);
            m_previewRender.camera.transform.rotation = Quaternion.Euler(0, 0, 0);
            m_previewRender.camera.fieldOfView = 60;
            m_previewRender.camera.cameraType = CameraType.SceneView;

            m_wireFrameMat = GetWireframeMaterial();
            
            m_serializedVertexPathIdx = serializedObject.FindProperty("m_vertexPathIdx");
            m_serializedMaterialArray = serializedObject.FindProperty("m_defaultMaterials");
            m_serializedGroundSnappingPivotIdx = serializedObject.FindProperty("m_groundSnappingPivotIdx");
            m_serializedRotation = serializedObject.FindProperty("m_rotation");

            SetupSmoothingGroups();

            m_propertyTree = PropertyTree.Create(m_config);
            m_propertyTree.OnPropertyValueChanged += PropertyChanged;
                    
            EditorApplication.update += Update;
        }

        private void PropertyChanged(InspectorProperty property, int selectionIndex)
        {
            if (property.Name == "m_mesh" || property.Name == "m_smoothingAngle")
                SetupSmoothingGroups();
        }
        
        private void OnDisable()
        {
            if (m_propertyTree != null)
            {
                m_propertyTree.OnPropertyValueChanged -= PropertyChanged;
                m_propertyTree.Dispose();
                m_propertyTree = null;
            }

            m_previewRender?.Cleanup();
            m_previewRender = null;

            if (m_wireFrameMat != null)
            {
                DestroyImmediate(m_wireFrameMat);
                m_wireFrameMat = null;
            }
            
            EditorApplication.update -= Update;
        }

        private void Update() => Repaint();

        public override bool HasPreviewGUI() => m_config != null && m_config.ProfileMesh != null;
        
        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            DoRotationControls(r);
            
            var matrix = GetMeshTransform();

            m_previewRender.camera.transform.position = matrix.MultiplyPoint(m_config.ProfileMesh.bounds.center) + Vector3.forward * m_camZoomFactor;
            m_previewRender.camera.transform.rotation = Quaternion.identity;
            
            m_previewRender.camera.nearClipPlane = 0.1f;
            m_previewRender.camera.farClipPlane = 1000f;

            m_previewRender.camera.fieldOfView = 60;
            
            if (Event.current.type == EventType.Repaint)
            {
                m_previewRender.BeginPreview(r, background);
                
                m_previewRender.DrawMesh(m_config.ProfileMesh, Vector3.zero, matrix.rotation, m_config.m_defaultMaterials[0], 0);
                m_previewRender.Render();

                GL.wireframe = true;
                m_previewRender.DrawMesh(m_config.ProfileMesh, Vector3.zero, matrix.rotation, m_wireFrameMat, 0);
                m_previewRender.Render();
                GL.wireframe = false;
            }
            
            //using (new Handles.DrawingScope())
            {
                // To handle resizing for gizmos since render will update it also based on rect size
                m_previewRender.camera.fieldOfView = HandlesCameraFOV(r);
                    
                Handles.SetCamera(m_previewRender.camera);
                
                Handles.PositionHandle(matrix.MultiplyPoint(m_config.ProfileMesh.bounds.center), matrix.rotation);

                GUIUtility.GetControlID(FocusType.Passive);
                
                serializedObject.Update();

                for (int group = 0; group < m_boundarySmoothingGroups.Count; group++)
                {
                    Random.InitState(group + 2);
                    Handles.color = Random.ColorHSV();
                    
                    for (int e = 0; e < m_boundarySmoothingGroups[group].Count-1; e++)
                    {
                        var edge1 = m_boundarySmoothingGroups[group][e];
                        var edge2 = m_boundarySmoothingGroups[group][e + 1];
                    
                        var p1 = matrix.MultiplyPoint(m_config.ProfileMesh.vertices[edge1.Vertex]);
                        var p2 = matrix.MultiplyPoint(m_config.ProfileMesh.vertices[edge2.Vertex]);
                    
                        Handles.DrawLine(p1, p2, 5f);
                        
                        if (e == 0)
                        {
                            Handles.Label(p1, $"Group: {group}");
                        }
                    }
                }
                foreach (var index in m_config.ProfileMesh.triangles)
                {
                    var vertex = matrix.MultiplyPoint(m_config.ProfileMesh.vertices[index]);
                    Handles.color = Color.red;
                    if (index == m_config.m_vertexPathIdx) Handles.color = Color.green;
                    if (Handles.Button(vertex, Quaternion.identity, 0.05f, 0.05f, Handles.SphereHandleCap)) 
                        m_serializedVertexPathIdx.intValue = index;
                    
                    //Handles.SphereHandleCap(index, vertex, Quaternion.identity, 0.05f, EventType.Layout);
                }
                
                Handles.color = Color.green;
                var groundSnappingVert = matrix.MultiplyPoint(m_config.ProfileMesh.vertices[m_config.m_groundSnappingPivotIdx]);
                Handles.DrawLine(groundSnappingVert, groundSnappingVert + m_config.m_rotation * Vector3.down, 2f);
                
                if (serializedObject.ApplyModifiedProperties())
                    MeshProfileConfig.OnModified?.Invoke(m_config);
            }
            
            if (Event.current.type == EventType.Repaint)
                m_previewRender.EndAndDrawPreview(r);
            
            if (Event.current.type == EventType.ScrollWheel)
            {
                m_camZoomFactor += 0.1f * HandleUtility.niceMouseDeltaZoom;
                m_camZoomFactor = Mathf.Clamp(m_camZoomFactor, -50f, -2f);
                Event.current.Use();
            }
        }

        private void DoRotationControls(Rect r)
        {
            var ogColor = GUI.backgroundColor;
            
            EditorGUILayout.BeginHorizontal();

            serializedObject.Update();
            
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("+X")) m_serializedRotation.quaternionValue *= Quaternion.Euler(90, 0, 0);
            if (GUILayout.Button("-X")) m_serializedRotation.quaternionValue *= Quaternion.Euler(-90, 0, 0);
            
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("+Y")) m_serializedRotation.quaternionValue *= Quaternion.Euler(0, 90, 0);
            if (GUILayout.Button("-Y")) m_serializedRotation.quaternionValue *= Quaternion.Euler(0, -90, 0);
            
            GUI.backgroundColor = Color.blue;
            if (GUILayout.Button("+Z")) m_serializedRotation.quaternionValue *= Quaternion.Euler(0, 0, 90);
            if (GUILayout.Button("-Z")) m_serializedRotation.quaternionValue *= Quaternion.Euler(0, 0, -90);

            if (serializedObject.ApplyModifiedProperties())
                MeshProfileConfig.OnModified?.Invoke(m_config);
            
            EditorGUILayout.EndHorizontal();
            
            GUI.backgroundColor = ogColor;
        }

        public override void OnInspectorGUI()
        {
            m_config ??= target as MeshProfileConfig;

            if (m_config.ProfileMesh != null)
            {
                serializedObject.Update();
                {
                    m_serializedVertexPathIdx.intValue = EditorGUILayout.IntSlider("Vertex Anchor",
                        m_config.m_vertexPathIdx, 0, m_config.ProfileMesh.vertexCount - 1);
                    m_serializedGroundSnappingPivotIdx.intValue = EditorGUILayout.IntSlider("Ground Snapping Pivot",
                        m_config.m_groundSnappingPivotIdx, 0, m_config.ProfileMesh.vertexCount - 1);
                    m_serializedMaterialArray.arraySize = m_boundarySmoothingGroups.Count;
                }
                if (serializedObject.ApplyModifiedProperties()) MeshProfileConfig.OnModified?.Invoke(m_config);
            }

            m_propertyTree.Draw();
        }

        private void SetupSmoothingGroups()
        {
            if (m_config.ProfileMesh == null) return;
            
            m_boundarySmoothingGroups = HalfEdge.BuildBoundarySmoothingGroups(m_config.ProfileMesh.vertices,
                HalfEdge.BakeHalfEdges(m_config.ProfileMesh.triangles), m_config.m_smoothingAngle);
        }
        private Matrix4x4 GetMeshTransform() => Matrix4x4.TRS(Vector3.zero, m_config.m_rotation, Vector3.one);
        private float HandlesCameraFOV(Rect r) => (float) ((double) Mathf.Atan((r.width <= 0 ? 1f : Mathf.Max(1f, (float) r.height / (float) r.width)) * Mathf.Tan((float) ((double) m_previewRender.camera.fieldOfView * 0.5 * (Math.PI / 180.0)))) * 57.295780181884766 * 2.0); 
        private Material GetWireframeMaterial()
        {
            var wireFrameCreate = typeof(MeshPreview).GetMethod("CreateWireframeMaterial", BindingFlags.Static | BindingFlags.NonPublic);
            return wireFrameCreate.Invoke(null, null) as Material;
        }
    }

#endif     
}