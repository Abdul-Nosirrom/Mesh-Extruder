using System.Linq;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.Splines;

#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor;
#endif

namespace FS.MeshProcessing
{
    [ExecuteAlways]
    public class BakedMeshVertexPath : MonoBehaviour, ISplineProvider
    {
        [Required, SerializeField] private ProBuilderMesh m_mesh;
        [SerializeField, HideInInspector] private Spline m_spline;

        [SerializeField, HideInInspector] private Edge[] m_edgeSelection;
        
        private void OnValidate()
        {
            // TODO: Custom edge loop selection to allow path generation w/out the need of probuilderizing the mesh!
            if (m_mesh == null)
                m_mesh = GetComponent<ProBuilderMesh>();
        }

        private void Update()
        {
            if (transform.hasChanged)
            {
                GenerateSpline();
                transform.hasChanged = false;
            }
        }
        
        public void GenerateSpline()
        {
            m_spline ??= new();
            m_spline.Clear();

            if (m_mesh == null)
            {
                Debug.LogError($"[{gameObject.name}] Attempting to generate spline with no mesh being set");
                return;
            }
            
            if (m_edgeSelection == null || m_edgeSelection.Length <= 1)
            {
                Debug.LogError("No edges selected, need at least 2 edges to create a spline");
                return;
            }

            var vertices = m_mesh.GetVertices();
            foreach (var edge in m_edgeSelection)
            {
                var startVertex = vertices[edge.a];
                var endVertex = vertices[edge.b];

                var startWorldPos = transform.TransformPoint(startVertex.position);
                var endWorldPos = transform.TransformPoint(endVertex.position);
                var normalWorld = transform.TransformDirection(startVertex.normal);
                
                var tangent = (endWorldPos - startWorldPos).normalized;
                var rot = Quaternion.LookRotation(tangent, normalWorld);
                
                BezierKnot knot = new BezierKnot(startWorldPos);
                knot.Rotation = rot;

                m_spline.Add(knot);
            }
        }

        public Spline GetSpline() => m_spline;
        
#if UNITY_EDITOR
        
        [Button("Regenerate Spline")]
        private void RegnerateSpline()
        {
            Undo.RecordObject(this, "Generating Edge Mesh Spline");
            GenerateSpline();
            EditorUtility.SetDirty(this);            
        }

        [Button("Bake Edge Spline")]
        private void SetupEdgeSpline()
        {
            Undo.RecordObject(this, "Generating Edge Mesh Spline");
            UpdateEdgeSelection();
            GenerateSpline();
            EditorUtility.SetDirty(this);            
        }
        
        public void UpdateEdgeSelection()
        {
            if (m_mesh == null) return;
            m_edgeSelection = m_mesh.selectedEdges.ToArray();
        }
        
        private void OnDrawGizmosSelected()
        {
            if (m_spline == null) return;

            int knotIdx = 0;
            foreach (var knot in m_spline.Knots)
            {
                Vector3 pos = knot.Position;
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(pos, 0.05f);

                Quaternion nativeRot = knot.Rotation;
                Gizmos.color = Color.green;
                Gizmos.DrawLine(pos, pos + 0.4f * (nativeRot * Vector3.up));
                
                //if (prevPos != Vector3.zero)
                {
                    Handles.BeginGUI();
                    Handles.Label(pos + nativeRot*Vector3.up, $"{knotIdx}");
                    Handles.EndGUI();
                }

                knotIdx++;
            }
        }
#endif   
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(BakedMeshVertexPath))]
    public class BakedMeshVertexPathEditor : OdinEditor
    {
        private BakedMeshVertexPath m_target;

        protected override void OnEnable()
        {
            base.OnEnable();
            m_target = target as BakedMeshVertexPath;
        }

        private void OnSceneGUI()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.control && e.keyCode == KeyCode.B)
                {
                    e.Use();
                    Undo.RecordObject(m_target, "Bake Mesh Vertex Path");
                    m_target.UpdateEdgeSelection();
                    m_target.GenerateSpline();
                    EditorUtility.SetDirty(m_target);
                }
            }
        }
    }
#endif    
}