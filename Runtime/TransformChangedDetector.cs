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
        public static event Action<GameObject> OnTransformChanged;

#if UNITY_EDITOR
        private void Start()
        {
            if (Application.isPlaying) gameObject.SetActive(false);
        }

        private void Update()
        {
            if (transform.hasChanged)
            {
                OnTransformChanged?.Invoke(gameObject);
                transform.hasChanged = false; // Set after as transform might be changed by event listeners (otherwise we get stuck always true)
            }
        }
#endif        
    }
}