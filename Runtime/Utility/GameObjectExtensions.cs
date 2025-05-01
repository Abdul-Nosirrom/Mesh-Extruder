using UnityEngine;

namespace FS.MeshProcessing.Utility
{
    public static class GameObjectExtensions
    {
        public static T GetOrAddComponent<T>(this GameObject go) where T : Component
        {
            return go.TryGetComponent(out T component) ? component : go.AddComponent<T>();
        }
        
        public static bool GetOrAddComponent<T>(this GameObject go, out T componentResult) where T : Component
        {
            bool doesExist = go.TryGetComponent(out T component);
            componentResult = component != null ? component : go.AddComponent<T>();
            return doesExist;
        }
    }
}