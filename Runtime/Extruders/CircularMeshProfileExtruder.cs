using Sirenix.OdinInspector;
using UnityEngine;

namespace FS.MeshProcessing
{
    public class CircularMeshProfileExtruder : MeshProfileExtruder
    {
        [Range(0, 1)] public float m_segmentsPerDegree = 0.5f;
        [ReadOnly] public float m_radius = 1f;
        [ReadOnly] public float m_angle = 30;
        
        public bool IsClosed => Mathf.Abs(Mathf.FloorToInt(m_angle)) == 360;
        
        protected override void GenerateExtrusionMatrices()
        {
            m_extrusionSegments.Clear();

            int numSegments = Mathf.FloorToInt(Mathf.Abs(m_angle * m_segmentsPerDegree));
            if (numSegments == 0) return;
            
            for (int i = 0; i <= numSegments; i++)
            {
                float angle = Mathf.Deg2Rad * m_angle * ((float) i / numSegments);
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                float z = m_radius * cos;
                float x = m_radius * sin;
                
                Vector3 pos = new Vector3(x, 0, z);
                Quaternion rot = Quaternion.LookRotation(new Vector3(cos, 0, -sin), Vector3.up);
                
                m_extrusionSegments.Add(Matrix4x4.TRS(pos, rot, Vector3.one));
            }
            
            ValidateProfilesTransform();        
        }

        protected override void EvaluateVertexPath(MeshVertexPath vertexPath)
        {
            vertexPath.m_spline.Closed = IsClosed;
        }
        
        protected override void ModifyExtrusionSettings(ref MeshProcessing.ExtrusionSettings extrusionSettings)
        {
            if (IsClosed)
            {
                extrusionSettings.BuildStartCap = false;
                extrusionSettings.BuildEndCap = false;
            }
        }
    }
}