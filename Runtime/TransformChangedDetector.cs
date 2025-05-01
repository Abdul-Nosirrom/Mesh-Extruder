using System;
using UnityEngine;

namespace FS.MeshProcessing
{
    /// <summary>
    /// Helper class to check if object was moved in editor and sends out an event.
    /// </summary>
    [ExecuteInEditMode]
    public class TransformChangedDetector : MonoBehaviour
    {
#if UNITY_EDITOR
        public static event Action<GameObject> OnTransformChanged;

        private void Awake()
        {
            if (Application.isPlaying) gameObject.SetActive(false);
        }

        private void Update()
        {
            if (transform.hasChanged)
            {
                OnTransformChanged?.Invoke(gameObject);
                transform.hasChanged = false;
            }
        }
#endif        
    }
}