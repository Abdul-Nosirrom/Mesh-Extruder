using System;
using Drawing;
using UnityEngine;
using UnityEngine.Splines;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FS.MeshProcessing
{
    public interface ISplineProvider
    {
        public Spline GetSpline();
        public Matrix4x4 LocalToWorldMatrix => (Component)this != null ? ((Component)this).transform.localToWorldMatrix : Matrix4x4.identity;
        public Matrix4x4 WorldToLocalMatrix => (Component)this != null ? ((Component)this).transform.worldToLocalMatrix : Matrix4x4.identity;
    }
    
    public class MeshVertexPath : MonoBehaviourGizmos, ISplineProvider
    {
        [field: SerializeField]
        public Spline m_spline { get; private set; }

        public Spline GetSpline() => m_spline;

        public void SetLocalSpaceVertexPath(BezierKnot[] vertexKnots)
        {
            m_spline ??= new();
            m_spline.Clear();
            
            foreach (var knot in vertexKnots)
            {
                m_spline.Add(knot, TangentMode.Linear);
            }
        }
        
#if UNITY_EDITOR
        public override void DrawGizmos()
        {
            if (!Selection.Contains(gameObject)) return;
            if (m_spline == null) return;

            using var thickness = Draw.WithLineWidth(2);

            var localToWorld = Application.isPlaying ? Matrix4x4.identity : transform.localToWorldMatrix;
            
            Vector3 prevPos = Vector3.zero;
            bool first = true;
            
            foreach (var knot in m_spline.Knots)
            {
                Vector3 pos = localToWorld.MultiplyPoint(knot.Position);
                Draw.WireSphere(pos, 0.05f, Color.red);

                Quaternion nativeRot = localToWorld.rotation * knot.Rotation;
                Draw.Arrow(pos, pos + 0.4f * (nativeRot * Vector3.up), Color.green);
                
                if (!first) Draw.Line(prevPos, pos, Color.purple);
                
                prevPos = pos;
                first = false;
            }
        }
#endif        
    }
}