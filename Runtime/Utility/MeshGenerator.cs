using JetBrains.Annotations;
using UnityEngine;

namespace FS.MeshProcessing
{
    public static class MeshGenerator
    {
        /// <summary>
        /// Generates a vertical cylinder mesh with customizable caps and vertex colors.
        /// UVs are world-space scaled (tiles around radius, stretches with height).
        /// </summary>
        /// <param name="radius">Cylinder radius</param>
        /// <param name="height">Cylinder height</param>
        /// <param name="radialSegments">Number of segments around the cylinder (higher = smoother)</param>
        /// <param name="createCaps">Whether to create top and bottom caps</param>
        /// <param name="capsColor">Vertex color for caps (default: black). Sides are always white.</param>
        /// <returns>Generated mesh</returns>
        public static Mesh GenerateCylinder(
            float radius, 
            float height, 
            int radialSegments = 32, 
            bool createCaps = true,
            Color? capsColor = null,
            [CanBeNull] string name = null)
        {
            Mesh mesh = new Mesh();
            mesh.name = name ?? "Procedural Cylinder";
            mesh.hideFlags = HideFlags.HideAndDontSave;

            Color capColor = capsColor ?? Color.black; // Default to black if not specified

            // Calculate vertex counts
            int sideVertCount = (radialSegments + 1) * 2; // +1 for UV seam, *2 for top and bottom
            int topCapVertCount = createCaps ? radialSegments + 1 : 0; // +1 for center
            int bottomCapVertCount = createCaps ? radialSegments + 1 : 0;
            int totalVertCount = sideVertCount + topCapVertCount + bottomCapVertCount;

            Vector3[] vertices = new Vector3[totalVertCount];
            Vector3[] normals = new Vector3[totalVertCount];
            Vector2[] uvs = new Vector2[totalVertCount];
            Color[] colors = new Color[totalVertCount];

            int vertIndex = 0;

            // === SIDE VERTICES ===
            int sideStartIndex = vertIndex;
            for (int i = 0; i <= radialSegments; i++)
            {
                float angle = (float)i / radialSegments * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                
                Vector3 normal = new Vector3(x, 0, z).normalized;

                // Bottom vertex
                vertices[vertIndex] = new Vector3(x, 0, z);
                normals[vertIndex] = normal;
                uvs[vertIndex] = new Vector2((float)i / radialSegments * (Mathf.PI * 2f * radius), 0); // World-space: circumference wraps
                colors[vertIndex] = Color.white; // Side = white
                vertIndex++;

                // Top vertex
                vertices[vertIndex] = new Vector3(x, height, z);
                normals[vertIndex] = normal;
                uvs[vertIndex] = new Vector2((float)i / radialSegments * (Mathf.PI * 2f * radius), height); // Height in world units
                colors[vertIndex] = Color.white; // Side = white
                vertIndex++;
            }

            int topCapStartIndex = vertIndex;
            int bottomCapStartIndex = vertIndex;

            if (createCaps)
            {
                // === TOP CAP VERTICES ===
                topCapStartIndex = vertIndex;
                
                // Center vertex
                vertices[vertIndex] = new Vector3(0, height, 0);
                normals[vertIndex] = Vector3.up;
                uvs[vertIndex] = new Vector2(0.5f, 0.5f); // Center of cap
                colors[vertIndex] = capColor; // Cap = custom color
                vertIndex++;

                // Ring vertices
                for (int i = 0; i < radialSegments; i++)
                {
                    float angle = (float)i / radialSegments * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * radius;
                    float z = Mathf.Sin(angle) * radius;

                    vertices[vertIndex] = new Vector3(x, height, z);
                    normals[vertIndex] = Vector3.up;
                    
                    // Radial UVs for cap
                    uvs[vertIndex] = new Vector2(
                        0.5f + Mathf.Cos(angle) * 0.5f,
                        0.5f + Mathf.Sin(angle) * 0.5f
                    );
                    colors[vertIndex] = capColor; // Cap = custom color
                    vertIndex++;
                }

                // === BOTTOM CAP VERTICES ===
                bottomCapStartIndex = vertIndex;
                
                // Center vertex
                vertices[vertIndex] = new Vector3(0, 0, 0);
                normals[vertIndex] = Vector3.down;
                uvs[vertIndex] = new Vector2(0.5f, 0.5f); // Center of cap
                colors[vertIndex] = capColor; // Cap = custom color
                vertIndex++;

                // Ring vertices
                for (int i = 0; i < radialSegments; i++)
                {
                    float angle = (float)i / radialSegments * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * radius;
                    float z = Mathf.Sin(angle) * radius;

                    vertices[vertIndex] = new Vector3(x, 0, z);
                    normals[vertIndex] = Vector3.down;
                    
                    // Radial UVs for cap (mirrored for correct winding)
                    uvs[vertIndex] = new Vector2(
                        0.5f + Mathf.Cos(angle) * 0.5f,
                        0.5f - Mathf.Sin(angle) * 0.5f
                    );
                    colors[vertIndex] = capColor; // Cap = custom color
                    vertIndex++;
                }
            }

            // === TRIANGLES ===
            int sideTriCount = radialSegments * 6; // 2 tris per quad, radialSegments quads
            int topCapTriCount = createCaps ? radialSegments * 3 : 0; // Triangle fan
            int bottomCapTriCount = createCaps ? radialSegments * 3 : 0;
            int totalTriCount = sideTriCount + topCapTriCount + bottomCapTriCount;

            int[] triangles = new int[totalTriCount];
            int triIndex = 0;

            // Side triangles
            for (int i = 0; i < radialSegments; i++)
            {
                int current = sideStartIndex + i * 2;
                int next = sideStartIndex + (i + 1) * 2;

                // First triangle (bottom-left, top-left, bottom-right)
                triangles[triIndex++] = current;
                triangles[triIndex++] = current + 1;
                triangles[triIndex++] = next;

                // Second triangle (bottom-right, top-left, top-right)
                triangles[triIndex++] = next;
                triangles[triIndex++] = current + 1;
                triangles[triIndex++] = next + 1;
            }

            if (createCaps)
            {
                // Top cap triangles (triangle fan from center)
                int topCenter = topCapStartIndex;
                for (int i = 0; i < radialSegments; i++)
                {
                    int current = topCapStartIndex + 1 + i;
                    int next = topCapStartIndex + 1 + ((i + 1) % radialSegments);

                    triangles[triIndex++] = topCenter;
                    triangles[triIndex++] = current;
                    triangles[triIndex++] = next;
                }

                // Bottom cap triangles (triangle fan from center, reversed winding)
                int bottomCenter = bottomCapStartIndex;
                for (int i = 0; i < radialSegments; i++)
                {
                    int current = bottomCapStartIndex + 1 + i;
                    int next = bottomCapStartIndex + 1 + ((i + 1) % radialSegments);

                    triangles[triIndex++] = bottomCenter;
                    triangles[triIndex++] = next; // Reversed winding for bottom face
                    triangles[triIndex++] = current;
                }
            }

            // Assign to mesh
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = triangles;

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            return mesh;
        }
    }
}