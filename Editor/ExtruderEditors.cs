using System;
using UnityEditor;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEngine;
using UnityEngine.Splines;

namespace FS.MeshProcessing.Editor
{
    #region Context Menu

    public static class MeshProfileCreateMenu
    {
        [MenuItem("GameObject/Mesh Extruder/Linear Mesh Profile Extruder", false, 10)]
        public static void CreateLinearMeshProfileExtruder(MenuCommand menuCommand) => Create<LinearMeshProfileExtruder>(menuCommand.context as GameObject);
        
        [MenuItem("GameObject/Mesh Extruder/Circular Mesh Profile Extruder", false, 10)]
        public static void CreateCircularMeshProfileExtruder(MenuCommand menuCommand) => Create<CircularMeshProfileExtruder>(menuCommand.context as GameObject);
        
        [MenuItem("GameObject/Mesh Extruder/Spiral Mesh Profile Extruder", false, 10)]
        public static void CreateSpiralMeshProfileExtruder(MenuCommand menuCommand) => Create<SpiralMeshProfileExtruder>(menuCommand.context as GameObject);
        
        [MenuItem("GameObject/Mesh Extruder/Spline Mesh Profile Extruder", false, 10)]
        public static void CreateSplineMeshProfileExtruder(MenuCommand menuCommand) => Create<SplineMeshProfileExtruder>(menuCommand.context as GameObject);

        public static GameObject Create<T>(GameObject parentContext, MeshProfileConfig profile = null) where T : MeshProfileExtruder 
            => Create(typeof(T), parentContext, profile);
        
        public static GameObject Create(Type extruderType, GameObject parentContext, MeshProfileConfig profile = null)
        {
            if (extruderType.IsAssignableFrom(typeof(MeshProfileConfig)))
            {
                Debug.LogError($"Cannot create extruder of type {extruderType.Name} as it is not a valid extruder type.");
                return null;
            }
            
            var go = new GameObject(extruderType.Name);
            go.AddComponent(extruderType);
            
            // Ensure it gets reparented if this was a context click (otherwise does nothing)
            GameObjectUtility.SetParentAndAlign(go, parentContext);
            
            Undo.RegisterCreatedObjectUndo(go, $"Create {extruderType.Name}");
            Selection.activeGameObject = go;
            return go;
        } 
    }
    
    #endregion
    
    #region Base

    [CanEditMultipleObjects]
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

            Tree.OnPropertyValueChanged += MaybeProfileChanged;
        }

        protected override void OnDisable()
        {
            Tree.OnPropertyValueChanged -= MaybeProfileChanged;
            base.OnDisable();
        }

        private void ResetProfile()
        {
            foreach (var t in targets)
            {
                Undo.RecordObject(t, "Reset Profile");
                
                var extruder = t as MeshProfileExtruder;
                extruder.ResetProfile();
                
                EditorUtility.SetDirty(t);
            }
        }

        private void MaybeProfileChanged(InspectorProperty property, int selectionIndex)
        {
            if (property.Name == "m_meshProfile")
            {
                ResetProfile();
            }
        }

        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Reset Profile"))
            {
                ResetProfile();
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

    [CanEditMultipleObjects]
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

    [CanEditMultipleObjects]
    [CustomEditor(typeof(CircularMeshProfileExtruder))]
    public class CircularMeshProfileExtruderEditor : MeshProfileExtruderEditor
    {
        protected override void OnSceneGUI()
        {
            base.OnSceneGUI();
            
            CircularMeshProfileExtruder extruder = (CircularMeshProfileExtruder)target;
            EditorGUI.BeginChangeCheck();
            
            HandlesUtility.ArcRadiusAngleHandle(extruder.transform.position, extruder.transform.rotation, extruder.m_radius, extruder.m_angle, out var radius, out var angle, 360);

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

    [CanEditMultipleObjects]
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

    [CanEditMultipleObjects]
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
            var color = value ? Color.green : Color.red;
            var ogColor = GUI.backgroundColor;
            GUI.backgroundColor = color;
            var buttonResult = GUILayout.Button(label);
            GUI.backgroundColor = ogColor;
            
            return buttonResult;
        }
    }

    #endregion
}
