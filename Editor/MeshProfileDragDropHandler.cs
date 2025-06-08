using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FS.MeshProcessing.Editor
{
    static class MeshProfileDragDropHandler
    {
        [InitializeOnLoadMethod]
        static void Load()
        {
            DragAndDrop.AddDropHandler(OnSceneDrop);
            DragAndDrop.AddDropHandler(OnHierarchyHandler);
        }
        
        private static DragAndDropVisualMode OnSceneDrop(Object dropUpon, Vector3 worldPosition, Vector2 viewportPosition, Transform parentForDraggedObjects, bool perform)
        {
            MeshProfileConfig meshProfile = DragAndDrop.objectReferences[0] as MeshProfileConfig;
            if (meshProfile)
            {
                if (perform)
                    ExtruderTypeMenu(meshProfile, worldPosition, parentForDraggedObjects);
                
                return DragAndDropVisualMode.Move;
            }

            return DragAndDropVisualMode.None;
        }

        private static DragAndDropVisualMode OnHierarchyHandler(int dropTargetInstanceID, HierarchyDropFlags dropMode, Transform parentForDraggedObjects, bool perform)
        {
            MeshProfileConfig meshProfile = DragAndDrop.objectReferences[0] as MeshProfileConfig;
            if (meshProfile)
            {
                if (perform)
                    ExtruderTypeMenu(meshProfile, parentForDraggedObjects ? parentForDraggedObjects.transform.position : Vector3.zero, parentForDraggedObjects);
                return DragAndDropVisualMode.Move;
            }

            return DragAndDropVisualMode.None;
        }

        private static void ExtruderTypeMenu(MeshProfileConfig profile, Vector3 position, Transform parent)
        {
            var menu = new GenericMenu();
            foreach (var type in TypeCache.GetTypesDerivedFrom<MeshProfileExtruder>())
            {
                if (type.IsAbstract || type.IsGenericType) continue;
                menu.AddItem(new GUIContent(type.Name), false, () =>
                {
                     /* Handle selection */
                     var go = MeshProfileCreateMenu.Create(type, parent?.gameObject, profile);
                     if (go)
                     {
                         go.GetComponent<MeshProfileExtruder>().MeshProfile = profile;
                         go.transform.position = position;
                     }
                });
            }
            
            menu.ShowAsContext();
        }
    }
}