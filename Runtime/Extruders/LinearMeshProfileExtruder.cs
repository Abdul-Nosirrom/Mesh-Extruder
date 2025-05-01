using Sirenix.OdinInspector;
using UnityEngine;

namespace FS.MeshProcessing
{
    public class LinearMeshProfileExtruder : MeshProfileExtruder
    {
        [Range(0, 10)] 
        public float m_segmentsPerMeter = 1f;
        [ReadOnly]
        public float m_distance = 5f;
        
        protected override void GenerateExtrusionMatrices()
        {
            m_extrusionSegments.Clear();
            int numSegments = Mathf.FloorToInt(Mathf.Abs(m_distance * m_segmentsPerMeter));
            if (numSegments == 0) return;
            
            for (int i = 0; i <= numSegments; i++)
            {
                Vector3 pos = Vector3.forward * (m_distance * (float)i / numSegments);
                m_extrusionSegments.Add(Matrix4x4.Translate(pos));
            }
            
            ValidateProfilesTransform();
        }
    }
}