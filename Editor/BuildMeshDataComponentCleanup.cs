using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FS.MeshProcessing.Editor
{
    public class BuildMeshDataComponentCleanup : IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            foreach (var obj in scene.GetRootGameObjects())
            {
                // Topology preservers are only needed in editor/import time
                foreach (var preserver in obj.GetComponentsInChildren<MeshTopologyPreserver>(true))
                {
                    Object.DestroyImmediate(preserver);
                }
                foreach (var edgeSelector in obj.GetComponentsInChildren<MeshEdgeSelector>(true))
                {
                    Object.DestroyImmediate(edgeSelector);
                }
            }
        }
    }
}