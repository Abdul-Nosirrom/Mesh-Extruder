using System;
using System.Collections.Generic;
using System.Numerics;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace FS.MeshProcessing
{
    public interface ISplineProvider
    {
        public Spline GetSpline();
    }
    
    public class MeshVertexPath : MonoBehaviour, ISplineProvider
    {
        [field: SerializeField]
        public Spline m_spline { get; private set; }

        public Spline GetSpline() => m_spline;

        public void SetVertexPath(BezierKnot[] vertexKnots)
        {
            m_spline ??= new();
            m_spline.Clear();
            
            foreach (var knot in vertexKnots)
            {
                m_spline.Add(knot, TangentMode.Linear);
            }
        }
        
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (m_spline == null) return;
            
            Vector3 prevPos = Vector3.zero;
            foreach (var knot in m_spline.Knots)
            {
                Vector3 pos = knot.Position;
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(pos, 0.05f);

                Quaternion nativeRot = knot.Rotation;
                Gizmos.color = Color.green;
                Gizmos.DrawLine(pos, pos + 0.4f * (nativeRot * Vector3.up));

                if (prevPos != Vector3.zero)
                {
                    Handles.BeginGUI();
                    Handles.Label(pos + nativeRot*Vector3.up, $"{Vector3.Distance(pos, prevPos)}");
                    Handles.EndGUI();
                }
                
                prevPos = pos;
            }
        }
#endif        
    }
}