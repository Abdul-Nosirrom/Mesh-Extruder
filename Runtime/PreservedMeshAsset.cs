using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FS.MeshProcessing
{
    public class PreservedMeshAsset : ScriptableObject
    {
        [SerializeField] private PreservedMesh m_mesh;
        public PreservedMesh Mesh => m_mesh;

        public void SetMesh(PreservedMesh mesh)
        {
            m_mesh = mesh;
            m_mesh?.ConfirmMesh();
        }

#if UNITY_EDITOR        
        public void DoGUI()
        {
            if (m_mesh == null)
            {
                EditorGUILayout.HelpBox("Incompatible gameobject, preserved mesh is setup upon import!", MessageType.Error);
                return;
            }
            
            GUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUILayout.Label($"[{m_mesh.m_name}] Mesh Info", EditorStyles.whiteLargeLabel);
            
            GUILayout.Label($"Vertex Count: {m_mesh.m_vertices.Length}");
            GUILayout.Label($"Face Count: {m_mesh.m_faces.Length}");
            GUILayout.Label($"HalfEdge Count: {m_mesh.m_halfEdges?.Length ?? -1}");

            GUILayout.Space(10);
            
            GUILayout.Label("___________");
            
            GUILayout.Label($"Tri Count: {m_mesh.m_triangleCount}");
            GUILayout.Label($"Quad Count: {m_mesh.m_quadCount}");
            GUILayout.Label($"NGon Count: {m_mesh.m_nGonCount}");
            
            GUILayout.EndVertical();
        }
#endif        
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(PreservedMeshAsset))]
    public class PreservedMeshAssetEditor : Editor
    {
        private PreservedMeshAsset m_target;
        private void OnEnable()
        {
            m_target = target as PreservedMeshAsset;
        }

        public override void OnInspectorGUI() => m_target.DoGUI();
    }
#endif    
}