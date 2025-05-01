using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace FS.MeshProcessing
{
    public class SplineMeshProfileExtruder : MeshProfileExtruder
    {
        [SerializeField] 
        private SplineContainer m_splineContainer;
        [SerializeField, ShowIf("m_splineContainer == null")] 
        private GameObject m_splineProvider;
        
        public Spline m_spline => m_splineContainer?.Spline ?? (m_splineProvider?.GetComponent<ISplineProvider>())?.GetSpline();
        
        [SerializeField, Range(0, 10)] 
        private int m_segmentsPerMeter = 1;

        [SerializeField, HideInInspector] 
        private bool m_sampleSplineLocalSpace = true;
        [SerializeField, HideInInspector] 
        private bool m_useAbsoluteDistance = false;
        
        [SerializeField, HideInInspector] 
        private float m_extrusionStartPercent = 0f;
        [SerializeField, HideInInspector] private float m_extrusionRangePercent = 0f;

        [SerializeField, HideInInspector] 
        private float m_extrusionStartDistance = 0f;
        [SerializeField, HideInInspector]
        private float m_extrusionRangeDistance = 0f;

        protected override void OnEnable()
        {
            base.OnEnable();
            Spline.Changed += SplineModified;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Spline.Changed -= SplineModified;
        }
        
        private void SplineModified(Spline spline, int knotIdx, SplineModification modType)
        {
            if (m_spline != null && m_spline == spline)
                GenerateMesh(true);//m_needsRegenerationFull = true;
        }

        protected override void GenerateExtrusionMatrices()
        {
            if (m_spline == null) return;
            
            float length = m_spline.CalculateLength(transform.localToWorldMatrix);
            int numSegments = Mathf.FloorToInt(Mathf.Abs(length * m_segmentsPerMeter));
            
            if (numSegments == 0) return;
            
            m_extrusionSegments.Clear();
            for (int i = 0; i <= numSegments; i++)
            {
                float t = m_extrusionStartDistance / length + (float)i / numSegments;
                if (t > 1f) break;
                if (t > (m_extrusionStartDistance + m_extrusionRangeDistance)/length) break;

                m_spline.Evaluate(t, out var pos, out var tangent, out var upVector);

                if (m_sampleSplineLocalSpace)
                {
                    pos = transform.worldToLocalMatrix.MultiplyPoint(pos);
                    tangent = transform.worldToLocalMatrix.MultiplyVector(tangent);
                    upVector = transform.worldToLocalMatrix.MultiplyVector(upVector);
                }

                var rotation = Quaternion.LookRotation(tangent, upVector);
                var matrix = Matrix4x4.TRS(pos, rotation, Vector3.one);
                m_extrusionSegments.Add(matrix);
            }
        }
        
#if UNITY_EDITOR
        protected override void Update()
        {
            // Ensure transform is synced up with the spline object
            if (m_spline != null)
            {
                var go = m_splineContainer?.transform ?? m_splineProvider?.transform;
                if (go)
                {
                    transform.position = go.position;
                    transform.rotation = go.rotation;
                    transform.localScale = go.localScale;
                }
            }
            base.Update();
        }
#endif        
    }

}