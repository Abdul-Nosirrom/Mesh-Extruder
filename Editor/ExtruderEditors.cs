using UnityEditor;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEngine;
using UnityEngine.Splines;

namespace FS.MeshProcessing.Editor
{
    #region Base

    [CustomEditor(typeof(MeshProfileExtruder))]
    public class MeshProfileExtruderEditor : OdinEditor
    {
        private SerializedProperty m_flipNormalsProperty;
        private SerializedProperty m_groundSnappingProperty;

        protected override void OnEnable()
        {
            base.OnEnable();
            m_flipNormalsProperty = serializedObject.FindProperty("m_flipNormals");
            m_groundSnappingProperty = serializedObject.FindProperty("m_snapToGround");
        }

        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Reset Profile"))
            {
                Undo.RecordObject(target, "Reset Profile");

                var extruder = target as MeshProfileExtruder;
                extruder.ResetProfile();
                
                EditorUtility.SetDirty(target);
            }

            base.OnInspectorGUI();
        }

        protected virtual void OnSceneGUI()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (!e.control && e.keyCode == KeyCode.N)
                {
                    e.Use();
                    serializedObject.Update();
                    m_flipNormalsProperty.boolValue = !m_flipNormalsProperty.boolValue;
                    serializedObject.ApplyModifiedProperties();
                }

                if (e.control && e.keyCode == KeyCode.G)
                {
                    e.Use();
                    serializedObject.Update();
                    m_groundSnappingProperty.boolValue = !m_groundSnappingProperty.boolValue;
                    serializedObject.ApplyModifiedProperties();
                }
            }
            
            MeshProfileExtruder extruder = (MeshProfileExtruder)target;
            var mesh = extruder.GeneratedMesh;
            Handles.Label(extruder.transform.position + extruder.transform.up,
                $"Mesh: {mesh.name}\n Vertices: {mesh.vertexCount}\nTriangles: {mesh.triangles.Length / 3}",
                SirenixGUIStyles.ButtonMid);
        }
    }

    #endregion

    #region Linear

    [CustomEditor(typeof(LinearMeshProfileExtruder))]
    public class LinearMeshProfileExtruderEditor : MeshProfileExtruderEditor
    {
        protected override void OnSceneGUI()
        {
            base.OnSceneGUI();
            
            LinearMeshProfileExtruder extruder = (LinearMeshProfileExtruder)target;
            EditorGUI.BeginChangeCheck();
            
            float dist = HandlesUtility.LinearScaleHandle(extruder.transform.position, extruder.transform.rotation, extruder.m_distance);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(extruder, "Rotate Handles");
                extruder.m_distance = dist;
                extruder.GenerateMesh(true);
            }
        }
    }

    #endregion


    #region Circular

    [CustomEditor(typeof(CircularMeshProfileExtruder))]
    public class CircularMeshProfileExtruderEditor : MeshProfileExtruderEditor
    {
        protected override void OnSceneGUI()
        {
            base.OnSceneGUI();
            
            CircularMeshProfileExtruder extruder = (CircularMeshProfileExtruder)target;
            EditorGUI.BeginChangeCheck();
            
            HandlesUtility.ArcRadiusAngleHandle(extruder.transform.position, extruder.transform.rotation, extruder.m_radius, extruder.m_angle, out var radius, out var angle);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(extruder, "Rotate Handles");
                extruder.m_angle = angle;
                extruder.m_radius = radius;
                extruder.GenerateMesh(true);
            }
        }
    }

    #endregion

    #region Spiral

    [CustomEditor(typeof(SpiralMeshProfileExtruder))]
    public class SpiralMeshProfileExtruderEditor : MeshProfileExtruderEditor
    {
        protected override void OnSceneGUI()
        {
            base.OnSceneGUI();
            
            SpiralMeshProfileExtruder extruder = (SpiralMeshProfileExtruder)target;
            EditorGUI.BeginChangeCheck();
            
            HandlesUtility.ArcRadiusAngleHandle(extruder.transform.position + extruder.transform.up * extruder.m_height, 
                extruder.transform.rotation, extruder.m_radius, extruder.m_angle, out var radius, out var angle);

            var scaleRot = Quaternion.LookRotation(extruder.transform.up, extruder.transform.forward);
            var height = HandlesUtility.LinearScaleHandle(extruder.transform.position, scaleRot, extruder.m_height);
            
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(extruder, "Rotate Handles");
                extruder.m_angle = angle;
                extruder.m_radius = radius;
                extruder.m_height = height;
                extruder.GenerateMesh(true);
            }
        }
    }

    #endregion

    #region Spline

    [CustomEditor(typeof(SplineMeshProfileExtruder))]
    public class SplineMeshProfileExtruderEditor : MeshProfileExtruderEditor
    {
        private SerializedProperty m_splineInLocalSpace;
        private SerializedProperty m_absoluteDistance;
        
        private SerializedProperty m_extrusionStartPercent;
        private SerializedProperty m_extrusionRangePercent;
        
        private SerializedProperty m_extrusionStartDistance;
        private SerializedProperty m_extrusionRangeDistance;
        
        protected override void OnEnable()
        {
            base.OnEnable();
            m_splineInLocalSpace = serializedObject.FindProperty("m_sampleSplineLocalSpace");
            m_absoluteDistance = serializedObject.FindProperty("m_useAbsoluteDistance");

            m_extrusionStartPercent = serializedObject.FindProperty("m_extrusionStartPercent");
            m_extrusionRangePercent = serializedObject.FindProperty("m_extrusionRangePercent");
            m_extrusionStartDistance = serializedObject.FindProperty("m_extrusionStartDistance");
            m_extrusionRangeDistance = serializedObject.FindProperty("m_extrusionRangeDistance");
        }
        
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            
            var extruder = target as SplineMeshProfileExtruder;
            if (extruder?.m_spline == null) return;

            var length = extruder.m_spline.CalculateLength(extruder.transform.localToWorldMatrix);
            serializedObject.Update();

            if (ToggleButton("Spline In Local Space", m_splineInLocalSpace.boolValue))
            {
                m_splineInLocalSpace.boolValue = !m_splineInLocalSpace.boolValue;
            }
            
            if (ToggleButton("Use Absolute Distance", m_absoluteDistance.boolValue))
            {
                m_absoluteDistance.boolValue = !m_absoluteDistance.boolValue;
            }
            
            if (m_absoluteDistance.boolValue)
            {
                // Convert distance to percentage
                if (length != 0)
                {
                    m_extrusionStartPercent.floatValue = m_extrusionStartDistance.floatValue / length;
                    m_extrusionRangePercent.floatValue = m_extrusionRangeDistance.floatValue / length;
                }
            }
            else
            {
                // Convert percentage to distance
                m_extrusionStartDistance.floatValue = m_extrusionStartPercent.floatValue * length;
                m_extrusionRangeDistance.floatValue = (1f - m_extrusionStartPercent.floatValue) * m_extrusionRangePercent.floatValue * length;
            }

            if (m_absoluteDistance.boolValue)
            {
                m_extrusionStartDistance.floatValue = EditorGUILayout.Slider("Start Distance", m_extrusionStartDistance.floatValue, 0, length);
                m_extrusionRangeDistance.floatValue = EditorGUILayout.Slider("Range", m_extrusionRangeDistance.floatValue, 0, length);
            }
            else
            {
                m_extrusionStartPercent.floatValue = EditorGUILayout.Slider("Start Percent", m_extrusionStartPercent.floatValue, 0, 1);
                m_extrusionRangePercent.floatValue = EditorGUILayout.Slider("Range", m_extrusionRangePercent.floatValue, 0, 1);
            }
            
            serializedObject.ApplyModifiedProperties();
        }

        private bool ToggleButton(string label, bool value)
        {
            var color = value ? Color.darkGreen : Color.darkRed;
            var ogColor = GUI.backgroundColor;
            GUI.backgroundColor = color;
            var buttonResult = GUILayout.Button(label);
            GUI.backgroundColor = ogColor;
            
            return buttonResult;
        }
    }

    #endregion
}