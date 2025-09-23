using Sirenix.OdinInspector;
using UnityEngine;

namespace FS.MeshProcessing
{
    public class SpiralMeshProfileExtruder : CircularMeshProfileExtruder
    {
        [ReadOnly] public float m_height = 0;
        
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
                float y = m_height * ((float)i / numSegments);
                
                Vector3 pos = new Vector3(x, y, z);
                // Tangent calculation - derivative of the spiral curve
                Vector3 tangent = new Vector3(
                    m_radius * cos * Mathf.Deg2Rad * m_angle / numSegments,  // dx/dt
                    m_height / numSegments,                                   // dy/dt  
                    -m_radius * sin * Mathf.Deg2Rad * m_angle / numSegments  // dz/dt
                ).normalized;
        
                Quaternion rot = Quaternion.LookRotation(tangent, Vector3.up);                
                m_extrusionSegments.Add(Matrix4x4.TRS(pos, rot, Vector3.one));
            }
            
            ValidateProfilesTransform();    
        }
    }
}