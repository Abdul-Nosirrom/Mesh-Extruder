using UnityEditor;
using UnityEngine;

namespace FS.MeshProcessing.Editor
{
    public static class HandlesUtility
    {
        public static void ArcRadiusAngleHandle(Vector3 position, Quaternion rotation, float radius, float angle, out float outRadius, out float outAngle)
        {
            outRadius = LinearScaleHandle(position, rotation, radius);
            outAngle = ArcAngleHandle(position, rotation, outRadius, angle);
        }

        public static float ArcAngleHandle(Vector3 position, Quaternion rotation, float radius, float angle)
        {
            var wsRot = rotation * Quaternion.Euler(0, angle, 0);

            // Rotation move handle, translation along tangent
            var rotPos = position + (wsRot * Vector3.forward) * radius;
            var tangent = wsRot * Vector3.right;

            Handles.color = Color.red;
            rotPos = Handles.FreeMoveHandle(rotPos, 0.5f, tangent * 0.1f, Handles.SphereHandleCap);

            Handles.color = Color.black;
            Handles.DrawAAPolyLine(5f, position, rotPos);
            
            angle += Vector3.Dot(rotPos - position, wsRot * Vector3.right * Mathf.Sign(radius));
        
        
            // Arc Draw
            {
                var color = angle < 0 ? Color.red : Color.yellow;
                color.a = 0.1f;
                Handles.color = color;
                Handles.DrawSolidArc(position, (rotation * Vector3.up),
                    (rotation * Vector3.forward),
                    angle, radius);

                Handles.color = Color.black;
                Handles.DrawWireArc(position, (rotation * Vector3.up),
                    (rotation * Vector3.forward),
                    angle, radius);
            }
            
            return Handles.SnapValue(angle, EditorSnapSettings.rotate);
        }

        public static float LinearScaleHandle(Vector3 position, Quaternion rotation, float size)
        {
            // Radius move handle, scale from center
            Vector3 radPos = position + (rotation * Vector3.forward) * size;
            Handles.color = (size < 0) ? Color.red : Color.green;
            Handles.DrawAAPolyLine(5f, position, radPos);

            radPos = Handles.FreeMoveHandle(radPos, 0.5f, (rotation * Vector3.forward) * 0.1f,
                Handles.SphereHandleCap);
            
            return Handles.SnapValue(Vector3.Dot(radPos - position, rotation * Vector3.forward),
                EditorSnapSettings.move.x);
        }
    }
}