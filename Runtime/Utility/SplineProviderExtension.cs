using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using ISplineProvider = FS.MeshProcessing.ISplineProvider;

namespace FS.MeshProcessing
{
    public static class SplineProviderExtension
    {
        public static bool IsClosed(this ISplineProvider spline) => spline.GetSpline().Closed;

        /// TODO: This length is in local-space, any scalling done is not taken into account!
        public static float GetLength(this ISplineProvider spline) => spline.GetSpline().GetLength();
        
        public static float GetNearestPoint(this ISplineProvider spline, Vector3 point, out Vector3 nearestPoint,
            out float time)
        {
            var localQueryPoint = spline.WorldToLocalMatrix.MultiplyPoint(point);
            float dist = SplineUtility.GetNearestPoint(spline.GetSpline(), localQueryPoint, out var nearestPointF, out time);
            nearestPoint = spline.LocalToWorldMatrix.MultiplyPoint(nearestPointF);
            return dist;
        }

        public static void Evaluate(this ISplineProvider spline, float time, out Vector3 position, out Vector3 tangent,
            out Vector3 upVector)
        {
            spline.GetSpline().Evaluate(time, out var localPos, out var localTangent, out var localUp);
            position = spline.LocalToWorldMatrix.MultiplyPoint(localPos);
            tangent = spline.LocalToWorldMatrix.MultiplyVector(localTangent).normalized;
            upVector = spline.LocalToWorldMatrix.MultiplyVector(localUp).normalized;
        }
        
        
        public static Vector3 EvaluatePosition(this ISplineProvider spline, float time)
        {
            var localPos = spline.GetSpline().EvaluatePosition(time);
            return spline.LocalToWorldMatrix.MultiplyPoint(localPos);
        }
        
        public static Vector3 EvaluateTangent(this ISplineProvider spline, float time)
        {
            var localTangent = spline.GetSpline().EvaluateTangent(time);
            return spline.LocalToWorldMatrix.MultiplyVector(localTangent).normalized;
        }
        
        public static Vector3 EvaluateUpVector(this ISplineProvider spline, float time)
        {
            var localUp = spline.GetSpline().EvaluateUpVector(time);
            return spline.LocalToWorldMatrix.MultiplyVector(localUp).normalized;
        }
        
        public static Vector3 EvaluateRightVector(this ISplineProvider spline, float time, Vector3? upOverride = null)
        {
            var tangent = spline.EvaluateTangent(time);
            var up = upOverride ?? spline.EvaluateUpVector(time);
            return Vector3.Cross(tangent, up).normalized;
        }
        
        public static Vector3 EvaluateAcceleration(this ISplineProvider spline, float time)
        {
            var localAccel = spline.GetSpline().EvaluateAcceleration(time);
            return spline.LocalToWorldMatrix.MultiplyVector(localAccel);
        }
    }
}