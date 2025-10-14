using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FS.MeshProcessing
{
    public static class WireframeDrawing
    {
        private static Mesh s_halfEdgeMesh;
        public static Mesh HalfEdgeMesh
        {
            get
            {
                if (s_halfEdgeMesh == null)
                {
                    s_halfEdgeMesh = new Mesh
                    {
                        name = "WireframeLine",
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    s_halfEdgeMesh.SetVertices(new[] // Half-Edge mesh (directed arrow kinda)
                    {
                        // Quad for 'arrow base'
                        new Vector3(0, 0, 0), // 0
                        new Vector3(0, 0, 0.9f), // 1
                        new Vector3(-0.1f, 0, 0), // 2
                        new Vector3(-0.1f, 0, 0.9f), // 3
                        
                        // Half Arrow head Tri
                        new Vector3(0, 0, 1), // 4
                        new Vector3(-0.2f, 0, 0.9f) // 5
                    });
                    s_halfEdgeMesh.SetNormals(new []
                    {
                        Vector3.up,
                        Vector3.up,
                        Vector3.up,
                        Vector3.up,
                        Vector3.up,
                        Vector3.up,
                    });
                    s_halfEdgeMesh.SetIndices(new[]
                    {
                        0, 1, 2,
                        2, 1, 3,
                        1, 4, 5,
                    }, MeshTopology.Triangles, 0);
                }

                return s_halfEdgeMesh;
            }
        }
        
        private static Material s_ngonWireframeMaterial;
        public static Material NgonWireframeMaterial
        {
            get
            {
                if (s_ngonWireframeMaterial == null)
                {
                    s_ngonWireframeMaterial = new Material(Shader.Find("Hidden/Editor/PreservedMeshWireframe"))
                    {
                        name = "NgonWireframeMaterial",
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    s_ngonWireframeMaterial.enableInstancing = true;
                }

                return s_ngonWireframeMaterial;
            }
        }

        private static RenderTexture s_selectionIDTexture;
        public static RenderTexture SelectionIDTexture
        {
            get
            {
                if (s_selectionIDTexture == null)
                {
                    s_selectionIDTexture = new RenderTexture(1, 1, 16, RenderTextureFormat.RFloat)
                    {
                        name = "SelectionIDTexture",
                        hideFlags = HideFlags.HideAndDontSave,
                        enableRandomWrite = true,
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        useMipMap = false,
                        autoGenerateMips = false,
                    };
                    s_selectionIDTexture.Create();
                }

                return s_selectionIDTexture;
            }
        }
        
        private static CommandBuffer s_commandBuffer;
        public static CommandBuffer CommandBuffer
        {
            get
            {
                return s_commandBuffer ??= new CommandBuffer
                {
                    name = "Wireframe Selection ID Buffer"
                };
            }
        }

        private static ComputeShader s_pixelReadbackCompute;

        public static ComputeShader PixelReadbackCompute
        {
            get
            {
                if (s_pixelReadbackCompute == null)
                    s_pixelReadbackCompute = Resources.Load<ComputeShader>("PixelReadbackCompute");
                return s_pixelReadbackCompute;
            }
        }
        
        #if UNITY_EDITOR
        private static Rect m_viewPort => SceneView.currentDrawingSceneView.cameraViewport;
        private static Camera m_camera => SceneView.currentDrawingSceneView.camera;
        #else
        private static Rect m_viewPort => Camera.main.rect;
        private static Camera m_camera => Camera.main;
        #endif
        

        public static void DrawWireframe(GraphicsBuffer edgeBuffer, Matrix4x4 localToWorld)
        {
            Profiler.BeginSample("Wireframe Drawing");
            
            MaterialPropertyBlock mp = new MaterialPropertyBlock();
            mp.SetMatrix("_LocalToWorld", localToWorld);
            mp.SetBuffer("_EdgeBuffer", edgeBuffer);

            {
                var viewport = m_viewPort;
                var viewportSize = new Vector2Int((int)viewport.width, (int)viewport.height);
                if (SelectionIDTexture.width != viewportSize.x || SelectionIDTexture.height != viewportSize.y)
                {
                    SelectionIDTexture.Release();
                    SelectionIDTexture.width = viewportSize.x;
                    SelectionIDTexture.height = viewportSize.y;
                    SelectionIDTexture.Create();
                }
                
                CommandBuffer.Clear();
                var cam = m_camera;

                var camColor = new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
                var selectionIDs = new RenderTargetIdentifier(SelectionIDTexture);
                
                CommandBuffer.SetRenderTarget(SelectionIDTexture);
                CommandBuffer.ClearRenderTarget(true, true, new Color(-1, -1, -1, -1));

                var RTs = new [] { camColor, selectionIDs };
                
                CommandBuffer.SetRenderTarget(RTs, camColor);
                CommandBuffer.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
                CommandBuffer.DrawMeshInstancedProcedural(
                    HalfEdgeMesh,
                    0,
                    NgonWireframeMaterial,
                    0,
                    edgeBuffer.count,
                    mp);
                Graphics.ExecuteCommandBuffer(CommandBuffer);
            }
            
            Profiler.EndSample();
        }
        
        
        public static int ReadSelectionID(Vector2 cursorPos)
        {
            // Simple screen space conversion
            int x = Mathf.FloorToInt(cursorPos.x);
            int y = Mathf.FloorToInt(SelectionIDTexture.height - cursorPos.y);  // Flip Y using Screen.height
    
            // Bounds check
            if (x < 0 || x >= SelectionIDTexture.width || y < 0 || y >= SelectionIDTexture.height)
                return -1;
    
            float[] result = new float[1] {-1};
            var buffer = new ComputeBuffer(1, sizeof(float));
            buffer.SetData(result);
            PixelReadbackCompute.SetVector("_CursorPos", new Vector4(x, y, 0, 0));
            PixelReadbackCompute.SetTexture(0, "_SelectionIDs", SelectionIDTexture);
            PixelReadbackCompute.SetBuffer(0, "_Result", buffer);
            PixelReadbackCompute.Dispatch(0, 1, 1, 1);
            buffer.GetData(result);
            buffer.Release();
    
            return (int)result[0];
        }
    }
}