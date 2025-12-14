using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using Assimp;
using FS.MeshProcessing.Utility;
using Unity.Mathematics;

namespace FS.MeshProcessing.Editor
{
    [CustomEditor(typeof(MeshTopologyPreserver))]
    public class MeshTopologyPreserverEditor : UnityEditor.Editor
    {
        private MeshTopologyPreserver m_target;

        private void OnEnable()
        {
            m_target = target as MeshTopologyPreserver;
        }

        public override void OnInspectorGUI()
        {
            m_target.DoGUI();
        }
    }
    
    public class PreserveTopologyImporter : AssetPostprocessor
    {
        //public bool PreserveTopology = true;

        // We should differentiate prefixes because w/ hmr for example we apply scaling, otherwise we dont
        private const string k_filePrefix = "hmr_";
        private const string k_meshPrefix = "preserve";
        
        private Dictionary<string, PreservedMesh> m_preservedMeshes = new();
        
        private void OnPreprocessModel()
        {
            // Read the source file of the mesh TODO: Make this a setting, we dont wanna do it on every mesh so should be a opt-in for the modelImporter
            ParseModelFile();
        }

        private void OnPostprocessModel(GameObject g)
        {
            Dictionary<string, PreservedMeshAsset> processedMeshes = new();
            
            // Read the source file of the mesh
            foreach (var mesh in m_preservedMeshes)
            {
                var child = g.transform.Find(mesh.Key);
                if (child)
                {
                    var meshFilter = child.GetComponent<MeshFilter>();
                    if (meshFilter == null) continue;

                    // We wanna set the PreservedMeshAsset as a sub-asset of the actual model asset
                    var meshAsset = meshFilter.sharedMesh;
                    var meshName = meshAsset.name;
                    var preservedMeshName = $"[Preserved Topology] {meshName}";
                    
                    // Does the asset already exist? Taking into account our degeneracy handler
                    if (!processedMeshes.ContainsKey(preservedMeshName))
                    {
                        var preservedMeshAsset = ScriptableObject.CreateInstance<PreservedMeshAsset>();
                        preservedMeshAsset.name = preservedMeshName;
                        preservedMeshAsset.SetMesh(mesh.Value);
                        context.AddObjectToAsset(preservedMeshName, preservedMeshAsset);
                        processedMeshes.Add(preservedMeshName, preservedMeshAsset);
                    }
                    
                    child.gameObject.AddComponent<MeshTopologyPreserver>().SetMesh(processedMeshes[preservedMeshName]);
                    Debug.LogError($"[Asset Importer] Preserved Topology for {mesh.Key} in {assetPath}");
                }
            }
        }
        
        private void ParseModelFile()
        {
            AssimpContext importer = new AssimpContext();

            try
            {
                var scene = importer.ImportFile(assetPath,
                    PostProcessSteps.JoinIdenticalVertices | PostProcessSteps.GenerateNormals |
                    PostProcessSteps.GenerateSmoothNormals); // | PostProcessSteps.MakeLeftHanded);

                // Figure out scale for vertices
                var modelImporter = assetImporter as ModelImporter;
                if (modelImporter == null) return;

                if (!assetPath.Contains(k_filePrefix)) return;

                m_preservedMeshes.Clear();

                float scaleFactor = modelImporter.globalScale;
                Debug.Log($"Importing w/ Scale Factor: {scaleFactor}");

                foreach (var mesh in scene.Meshes)
                {
                    // Check if the name ends with "Mesh"
                    string meshName = mesh.Name;
                    //if (!meshName.EndsWith("Mesh")) continue;

                    // Remove it
                    if (meshName.EndsWith("Mesh"))
                        meshName = meshName.Substring(0, meshName.Length - 4);
                    Debug.LogError($"Attempting To Process: {meshName}");
                    if (!meshName.StartsWith(k_meshPrefix)) continue;

                    var preservedMesh = new PreservedMesh();
                    preservedMesh.m_name = meshName;
                    preservedMesh.m_vertices = new Vertex[mesh.VertexCount];
                    preservedMesh.m_faces = new Face[mesh.FaceCount];
                    m_preservedMeshes.Add(meshName, preservedMesh);

                    Debug.LogError(
                        $"Processing Mesh: {meshName} with {mesh.VertexCount} vertices and {mesh.FaceCount} faces.");

                    HashSet<int> zeroNormalVertices = new HashSet<int>();

                    // Copy vertices and normals
                    bool allZeroNormals = true;
                    for (int i = 0; i < mesh.VertexCount; i++)
                    {
                        preservedMesh.m_vertices[i] = new Vertex();
                        preservedMesh.m_vertices[i].m_numFaces =
                            0; // We'll iterate on this when parsing the faces later

                        var v = mesh.Vertices[i];
                        preservedMesh.m_vertices[i].m_position = scaleFactor * new Vector3(-v.X, v.Y, v.Z);

                        if (mesh.HasNormals)
                        {
                            var n = mesh.Normals[i];
                            preservedMesh.m_vertices[i].m_normal =
                                Vector3.Normalize(new Vector3(-n.X, n.Y,
                                    n.Z)); // TODO: From hammer x is inverted, lefthanded-ness fixes vertex position but not normals for some reason?

                            if (preservedMesh.m_vertices[i].m_normal is { x: <= 0, y: <= 0, z: <= 0 })
                                zeroNormalVertices.Add(i);
                            else allZeroNormals = false;
                        }
                    }

                    // Copy faces
                    int quadCount = 0;
                    int triCount = 0;
                    int nGonCount = 0;
                    for (int i = 0; i < mesh.FaceCount; i++)
                    {
                        var assimpFace = mesh.Faces[i];
                        Face face = new Face();
                        face.m_vertexIndices = assimpFace.Indices.ToArray();

                        preservedMesh.m_faces[i] = face;

                        foreach (var vertex in face.m_vertexIndices)
                            preservedMesh.m_vertices[vertex].m_numFaces++;

                        if (face.m_vertexIndices.Length == 3) triCount++;
                        else if (face.m_vertexIndices.Length == 4) quadCount++;
                        else nGonCount++;
                    }

                    // NOTE: In non-solid meshes, for some reason the normals are zero on import. Assuming planar quads/ngons - we'll just calculate them here
                    if (!allZeroNormals) zeroNormalVertices.Clear();
                    foreach (var vIdx in zeroNormalVertices)
                    {
                        List<float3> accumulatedNormals = new List<float3>();
                        // Find a faces that uses this vertex, accumulate their normals & average them
                        foreach (var face in preservedMesh.m_faces)
                        {
                            if (face.m_vertexIndices.Contains(vIdx))
                            {
                                // Calculate normal for this face
                                if (face.m_vertexIndices.Length < 3)
                                    continue; // Can't calculate normal with less than 3 verts

                                // Assume faces are planar, so we can just use the first 3 vertices to calculate the normal
                                var v0 = preservedMesh.m_vertices[face.m_vertexIndices[0]].m_position;
                                var v1 = preservedMesh.m_vertices[face.m_vertexIndices[1]].m_position;
                                var v2 = preservedMesh.m_vertices[face.m_vertexIndices[2]].m_position;

                                // Calculate the average face normal
                                float3 faceNormal = math.normalize(math.cross(v2 - v1, v1 - v0));
                                accumulatedNormals.Add(faceNormal);
                            }
                        }

                        // Average the normals
                        float3 avgNormal = float3.zero;
                        foreach (var n in accumulatedNormals)
                            avgNormal += n;
                        avgNormal = math.normalize(avgNormal / accumulatedNormals.Count);

                        // Normalize the vertex normal
                        preservedMesh.m_vertices[vIdx].m_normal = new float3(avgNormal.x, avgNormal.y, avgNormal.z);
                    }

                    preservedMesh.m_triangleCount = triCount;
                    preservedMesh.m_quadCount = quadCount;
                    preservedMesh.m_nGonCount = nGonCount;

                    WeldVertices(preservedMesh, smoothingAngleDegrees: modelImporter.normalSmoothingAngle);

                    // TODO: this is kinda shitty and hacky but gets the job done rn for solving the non-closed mesh problem. Ideally we'd move the face normal calculation into the weld step and do it properly
                    if (allZeroNormals)
                        SplitVerticesForHardEdges(preservedMesh, hardAngleDegrees: modelImporter.normalSmoothingAngle);

                    // Should we quadify this mesh? Try it
                    if (mesh.Name.ToLower().Contains("tri"))
                    {
                        preservedMesh.Quadify();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"ASSIMP Failed to import model for preserved topology at {assetPath}: {e.Message}");
            }
        }
        
        private void WeldVertices(PreservedMesh mesh, float positionTolerance = 0.0001f, float smoothingAngleDegrees = 80f)
        {
            float cosAngleThreshold = Mathf.Cos(smoothingAngleDegrees * Mathf.Deg2Rad);
            
            // Build a map from old vertex indices to new welded indices
            Dictionary<Vector3, List<int>> vertexGroups = new Dictionary<Vector3, List<int>>();
            
            // Group vertices by position (with tolerance)
            for (int i = 0; i < mesh.m_vertices.Length; i++)
            {
                Vector3 pos = mesh.m_vertices[i].m_position;
                Vector3 roundedPos = new Vector3(
                    Mathf.Round(pos.x / positionTolerance) * positionTolerance,
                    Mathf.Round(pos.y / positionTolerance) * positionTolerance,
                    Mathf.Round(pos.z / positionTolerance) * positionTolerance
                );
                
                if (!vertexGroups.ContainsKey(roundedPos))
                    vertexGroups[roundedPos] = new List<int>();
                vertexGroups[roundedPos].Add(i);
            }
            
            // Create remapping table and new vertex list
            int[] remapTable = new int[mesh.m_vertices.Length];
            List<Vertex> newVertices = new List<Vertex>();
            
            foreach (var group in vertexGroups.Values)
            {
                if (group.Count == 1)
                {
                    // Single vertex at this position - keep as-is
                    remapTable[group[0]] = newVertices.Count;
                    newVertices.Add(mesh.m_vertices[group[0]]);
                }
                else
                {
                    // Multiple vertices at same position - check normals for smoothing
                    List<List<int>> smoothGroups = new List<List<int>>();
                    
                    foreach (int vertIndex in group)
                    {
                        Vector3 normal = mesh.m_vertices[vertIndex].m_normal;
                        bool foundGroup = false;
                        
                        // Try to add to existing smooth group
                        for (int g = 0; g < smoothGroups.Count; g++)
                        {
                            Vector3 groupNormal = mesh.m_vertices[smoothGroups[g][0]].m_normal;
                            if (Vector3.Dot(normal, groupNormal) >= cosAngleThreshold)
                            {
                                smoothGroups[g].Add(vertIndex);
                                foundGroup = true;
                                break;
                            }
                        }
                        
                        // Create new smooth group if needed
                        if (!foundGroup)
                        {
                            smoothGroups.Add(new List<int> { vertIndex });
                        }
                    }
                    
                    // Create one vertex per smooth group
                    foreach (var smoothGroup in smoothGroups)
                    {
                        // Average the normals in the smooth group
                        float3 avgNormal = float3.zero;
                        int totalFaces = 0;
                        
                        foreach (int idx in smoothGroup)
                        {
                            avgNormal += mesh.m_vertices[idx].m_normal;
                            totalFaces += mesh.m_vertices[idx].m_numFaces;
                        }
                        avgNormal = math.normalize(avgNormal);
                        
                        // Create merged vertex
                        Vertex mergedVertex = new Vertex
                        {
                            m_position = mesh.m_vertices[smoothGroup[0]].m_position,
                            m_normal = avgNormal,
                            m_numFaces = 0 // Will be recalculated
                        };
                        
                        int newIndex = newVertices.Count;
                        newVertices.Add(mergedVertex);
                        
                        // Map all vertices in this smooth group to the new vertex
                        foreach (int idx in smoothGroup)
                        {
                            remapTable[idx] = newIndex;
                        }
                    }
                }
            }
            
            // Update face indices and recalculate face counts
            for (int i = 0; i < mesh.m_faces.Length; i++)
            {
                for (int j = 0; j < mesh.m_faces[i].m_vertexIndices.Length; j++)
                {
                    int oldIndex = mesh.m_faces[i].m_vertexIndices[j];
                    int newIndex = remapTable[oldIndex];
                    //var vertex = newVertices[newIndex];
                    mesh.m_faces[i].m_vertexIndices[j] = newIndex;
                    //vertex.m_numFaces++;
                    //newVertices[newIndex] = vertex;
                }
            }
            
            // Replace vertex array
            mesh.m_vertices = newVertices.ToArray();
            for (var index = 0; index < mesh.m_vertices.Length; index++)
            {
                mesh.m_vertices[index].m_numFaces = 0;
            }

            // Update face count
            foreach (var face in mesh.m_faces)
            {
                foreach (var vIdx in face.m_vertexIndices)
                {
                    var vertex = mesh.m_vertices[vIdx];
                    vertex.m_numFaces++;
                    mesh.m_vertices[vIdx] = vertex;
                }
            }
            
            Debug.Log($"Welded vertices: {remapTable.Length} -> {newVertices.Count} " +
                      $"(merged {remapTable.Length - newVertices.Count} duplicates)");
        }
        
        private void SplitVerticesForHardEdges(PreservedMesh mesh, float hardAngleDegrees = 30f)
        {
            float cosAngleThreshold = Mathf.Cos(hardAngleDegrees * Mathf.Deg2Rad);
            
            // First, compute face normals from vertex positions
            Vector3[] faceNormals = new Vector3[mesh.m_faces.Length];
            for (int i = 0; i < mesh.m_faces.Length; i++)
            {
                var face = mesh.m_faces[i];
                if (face.m_vertexIndices.Length >= 3)
                {
                    // Get first 3 vertices to compute face normal
                    Vector3 v0 = mesh.m_vertices[face.m_vertexIndices[0]].m_position;
                    Vector3 v1 = mesh.m_vertices[face.m_vertexIndices[1]].m_position;
                    Vector3 v2 = mesh.m_vertices[face.m_vertexIndices[2]].m_position;
                    
                    // Compute face normal using cross product
                    Vector3 edge1 = v2 - v1;
                    Vector3 edge2 = v1 - v0;
                    faceNormals[i] = Vector3.Cross(edge1, edge2).normalized;
                }
            }
            
            // Build edge-to-faces mapping
            // Edge is defined by two vertex indices (always store smaller index first)
            Dictionary<(int, int), List<int>> edgeToFaces = new Dictionary<(int, int), List<int>>();
            
            for (int faceIdx = 0; faceIdx < mesh.m_faces.Length; faceIdx++)
            {
                var face = mesh.m_faces[faceIdx];
                int vertCount = face.m_vertexIndices.Length;
                
                for (int i = 0; i < vertCount; i++)
                {
                    int v0 = face.m_vertexIndices[i];
                    int v1 = face.m_vertexIndices[(i + 1) % vertCount];
                    
                    // Normalize edge representation (smaller index first)
                    var edge = v0 < v1 ? (v0, v1) : (v1, v0);
                    
                    if (!edgeToFaces.ContainsKey(edge))
                        edgeToFaces[edge] = new List<int>();
                    edgeToFaces[edge].Add(faceIdx);
                }
            }
            
            // Find vertices that need to be split
            // Map from vertex index to list of face groups (faces that should share the same vertex)
            Dictionary<int, List<HashSet<int>>> vertexFaceGroups = new Dictionary<int, List<HashSet<int>>>();
            
            // Initialize with all faces using each vertex
            for (int vertIdx = 0; vertIdx < mesh.m_vertices.Length; vertIdx++)
            {
                vertexFaceGroups[vertIdx] = new List<HashSet<int>>();
                HashSet<int> initialGroup = new HashSet<int>();
                
                // Find all faces using this vertex
                for (int faceIdx = 0; faceIdx < mesh.m_faces.Length; faceIdx++)
                {
                    if (mesh.m_faces[faceIdx].m_vertexIndices.Contains(vertIdx))
                    {
                        initialGroup.Add(faceIdx);
                    }
                }
                
                if (initialGroup.Count > 0)
                    vertexFaceGroups[vertIdx].Add(initialGroup);
            }
            
            // Split groups based on hard edges
            foreach (var edgeEntry in edgeToFaces)
            {
                if (edgeEntry.Value.Count < 2) continue; // Not a shared edge
                
                var edge = edgeEntry.Key;
                var faces = edgeEntry.Value;
                
                // Check angle between face normals
                bool shouldSplit = false;
                for (int i = 0; i < faces.Count - 1; i++)
                {
                    for (int j = i + 1; j < faces.Count; j++)
                    {
                        float dot = Vector3.Dot(faceNormals[faces[i]], faceNormals[faces[j]]);
                        if (dot < cosAngleThreshold)
                        {
                            shouldSplit = true;
                            break;
                        }
                    }
                    if (shouldSplit) break;
                }
                
                if (shouldSplit)
                {
                    // Split the vertex groups for both vertices of this edge
                    int[] edgeVerts = { edge.Item1, edge.Item2 };
                    
                    foreach (int vertIdx in edgeVerts)
                    {
                        var groups = vertexFaceGroups[vertIdx];
                        
                        // Find which groups contain faces from this edge
                        for (int g = 0; g < groups.Count; g++)
                        {
                            var group = groups[g];
                            var facesInGroup = faces.Where(f => group.Contains(f)).ToList();
                            
                            if (facesInGroup.Count > 1)
                            {
                                // Need to split this group based on normal similarity
                                var remainingFaces = new HashSet<int>(facesInGroup);
                                var newGroups = new List<HashSet<int>>();
                                
                                while (remainingFaces.Count > 0)
                                {
                                    var seedFace = remainingFaces.First();
                                    var newGroup = new HashSet<int> { seedFace };
                                    remainingFaces.Remove(seedFace);
                                    
                                    // Add faces with similar normals to this group
                                    var toAdd = remainingFaces.Where(f => 
                                        Vector3.Dot(faceNormals[seedFace], faceNormals[f]) >= cosAngleThreshold
                                    ).ToList();
                                    
                                    foreach (var f in toAdd)
                                    {
                                        newGroup.Add(f);
                                        remainingFaces.Remove(f);
                                    }
                                    
                                    newGroups.Add(newGroup);
                                }
                                
                                // Remove faces that were split from the original group
                                foreach (var f in facesInGroup)
                                    group.Remove(f);
                                
                                // Add the new groups
                                groups.AddRange(newGroups);
                                
                                // Remove the original group if it's now empty
                                if (group.Count == 0)
                                    groups.Remove(group);
                            }
                        }
                    }
                }
            }
            
            // Create new vertices and build remapping
            List<Vertex> newVertices = new List<Vertex>();
            Dictionary<(int, HashSet<int>), int> vertexGroupToNewIndex = new Dictionary<(int, HashSet<int>), int>();
            
            foreach (var kvp in vertexFaceGroups)
            {
                int originalVertIdx = kvp.Key;
                var groups = kvp.Value;
                
                foreach (var group in groups)
                {
                    if (group.Count == 0) continue;
                    
                    // Create a new vertex for this group
                    Vertex newVertex = mesh.m_vertices[originalVertIdx];
                    newVertex.m_numFaces = 0; // Will be recalculated
                    
                    // Optionally: compute new normal as average of face normals in the group
                    Vector3 avgNormal = Vector3.zero;
                    foreach (int faceIdx in group)
                    {
                        avgNormal += faceNormals[faceIdx];
                    }
                    newVertex.m_normal = avgNormal.normalized;
                    
                    int newIndex = newVertices.Count;
                    newVertices.Add(newVertex);
                    vertexGroupToNewIndex[(originalVertIdx, group)] = newIndex;
                }
            }
            
            // Update face indices
            for (int faceIdx = 0; faceIdx < mesh.m_faces.Length; faceIdx++)
            {
                var face = mesh.m_faces[faceIdx];
                
                for (int v = 0; v < face.m_vertexIndices.Length; v++)
                {
                    int oldVertIdx = face.m_vertexIndices[v];
                    
                    // Find which group this face belongs to for this vertex
                    foreach (var group in vertexFaceGroups[oldVertIdx])
                    {
                        if (group.Contains(faceIdx))
                        {
                            face.m_vertexIndices[v] = vertexGroupToNewIndex[(oldVertIdx, group)];
                            break;
                        }
                    }
                }
                
                mesh.m_faces[faceIdx] = face;
            }
            
            // Replace vertex array
            int originalCount = mesh.m_vertices.Length;
            mesh.m_vertices = newVertices.ToArray();
            
            // Recalculate face counts
            for (int i = 0; i < mesh.m_vertices.Length; i++)
            {
                mesh.m_vertices[i].m_numFaces = 0;
            }
            
            foreach (var face in mesh.m_faces)
            {
                foreach (var vIdx in face.m_vertexIndices)
                {
                    var vertex = mesh.m_vertices[vIdx];
                    vertex.m_numFaces++;
                    mesh.m_vertices[vIdx] = vertex;
                }
            }
            
            Debug.Log($"Split vertices for hard edges: {originalCount} -> {newVertices.Count} " +
                      $"(created {newVertices.Count - originalCount} new vertices for hard edges at {hardAngleDegrees}°)");
        }
    }
}