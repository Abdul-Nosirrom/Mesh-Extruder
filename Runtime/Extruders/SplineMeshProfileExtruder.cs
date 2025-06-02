using System;
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
        [SerializeField] 
        private GameObject m_splineProvider;
        
        public Spline m_spline => m_splineContainer?.Spline ?? (m_splineProvider?.GetComponent<ISplineProvider>())?.GetSpline();

        [SerializeField] 
        private bool m_lockTransformToSpline = true;
        
        [SerializeField, Range(0, 10)] 
        private int m_segmentsPerMeter = 1;

        // Its setup in editor
#pragma warning disable CS0414 

        [SerializeField, HideInInspector] 
        private bool m_sampleSplineLocalSpace = true;
        [SerializeField, HideInInspector] 
        private bool m_useAbsoluteDistance = false;
        
        [SerializeField, HideInInspector] 
        private float m_extrusionStartPercent = 0f;
        [SerializeField, HideInInspector] private float m_extrusionRangePercent = 0f;
        
#pragma warning restore CS0414
        
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

        protected override void ModifyExtrusionSettings(ref MeshProcessing.ExtrusionSettings extrusionSettings)
        {
            if (m_spline != null && m_spline.Closed)
            {
                extrusionSettings.BuildStartCap = false;
                extrusionSettings.BuildEndCap = false;
            }
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
            if (Application.isPlaying) return;
            
            // Ensure transform is synced up with the spline object
            if (m_spline != null && m_lockTransformToSpline)
            {
                var go = m_splineContainer?.transform ?? m_splineProvider?.transform;
                if (go && go.hasChanged)
                {
                    transform.position = go.position;
                    transform.rotation = go.rotation;
                    transform.localScale = go.localScale;
                    
                    go.hasChanged = false;
                }
            }
            
            base.Update();
        }

        private void OnDrawGizmosSelected()
        {
            if (m_spline == null) return;
            
            float length = m_spline.CalculateLength(transform.localToWorldMatrix);
            int numSegments = Mathf.FloorToInt(Mathf.Abs(length * m_segmentsPerMeter));
            var extents = m_editableProfileObject.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            float xBounds = extents.z;
            if (numSegments == 0) return;
            
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

                CurvatureEvaluation(t, tangent, upVector, out var curvature, out var isConcave);
                Gizmos.color = isConcave ? Color.red : Color.green;
                    

                Vector3 posV = pos;
                posV += transform.position;
                var rightVector = Vector3.Normalize(Vector3.Cross(upVector, tangent));
                Gizmos.DrawLine(posV, posV  + rightVector * curvature);
                //Gizmos.DrawWireCube(posV, m_editableProfileObject.GetComponent<MeshFilter>().sharedMesh.bounds.);
                //Gizmos.DrawSphere(posV, 0.25f);
                Gizmos.color = Color.darkOrange;
                Gizmos.DrawLine(posV + Vector3.up, posV + rightVector * xBounds + Vector3.up);
            }
        }

        private Matrix4x4 CurvatureEvaluation(float t, Vector3 tangent, Vector3 upVector, out float k, out bool isConcave)
        {
            k = m_spline.EvaluateCurvature(t) * tangent.magnitude;

            Vector3 secondDeriv = m_spline.EvaluateAcceleration(t);
            Vector3 normal = Vector3.Cross(upVector, tangent);

            isConcave = Vector3.Dot(normal, secondDeriv) > 0;
            return Matrix4x4.identity;

            // if (!isConcave) return Matrix4x4.identity;
            //
            // // Calculate the curvature matrix, scaling by the bounds to curvature ratio
            // float xBounds = (m_meshProfile.m_rotation * m_meshProfile.ProfileMesh.bounds.size).x;
            // if (xBounds > k)
            // {
            //     isConcave = false;
            //     return Matrix4x4.identity;
            // }
            // float scale = xBounds / k;
            // return Matrix4x4.identity;
        }
#endif        
    }

}