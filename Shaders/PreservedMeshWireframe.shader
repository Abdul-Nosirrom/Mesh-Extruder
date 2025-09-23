Shader "Hidden/Editor/PreservedMeshWireframe"
{
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "Queue"="Geometry"
            "RenderPipeline"="UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "PreservedMeshWireframe"
            Tags
            {
                "LightMode"="UniversalForward"
            }
            Cull Off
            ZWrite Off
            ZTest Less
            
            // Opaque

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            //#pragma instancing_options procedural:setup
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct EdgeData
            {
                float3 positionA;
                float3 positionB;
                float3 normal;
                float3 right;
                float4 color;
            };
            
            struct appdata
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 color : TEXCOORD2;
                nointerpolation uint instanceID : TEXCOORD3;
            };

            struct output
            {
                float4 color : SV_Target;
                float id : SV_Target1;
            };

            
            float4x4 _LocalToWorld;
            StructuredBuffer<EdgeData> _EdgeBuffer;

            void setup()
            {
                unity_ObjectToWorld = _LocalToWorld;
                unity_WorldToObject = unity_ObjectToWorld;
                unity_WorldToObject._14_24_34 *= -1;
                unity_WorldToObject._11_22_33 = 1.0f / unity_WorldToObject._11_22_33;
            }


            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                
                
                // So, here's the deal.
                // We wanna have the mesh normal be eqal to the edge normal we're dealing with
                // Becase of how the mesh is laid out, the directionality of the half-edge will be correct
                // if the mesh is properly aligned w/ the normal. This is a rotation.
                // - Basis where
                // -- Z (forward) is along the edge direction
                // -- Y (up) is along the edge normal
                // -- X (right) is the cross product of those two
                // - Position, (0,0,0) corresponds to edge.positionA
                // - Scale, X is thickness, Z is edgeLength
                

                EdgeData edge = _EdgeBuffer[instanceID];// (EdgeData)0;
                //#if UNITY_ANY_INSTANCING_ENABLED
                //edge = _EdgeBuffer[v.instanceID];
                //#endif

                unity_ObjectToWorld = _LocalToWorld;

                // Transform edge data to world space
                edge.positionA = TransformObjectToWorld(float4(edge.positionA + edge.normal * 0.01f, 1)).xyz;
                edge.positionB = TransformObjectToWorld(float4(edge.positionB + edge.normal * 0.01f, 1)).xyz;
                edge.normal = TransformObjectToWorldDir(edge.normal);
                edge.right = TransformObjectToWorldDir(edge.right);
                
                // Transform the mesh vertices based on edge start/end positions
                float3 edgeDir = edge.positionB - edge.positionA;
                float edgeLength = length(edgeDir);
                edgeDir = normalize(edgeDir);

                float3 forward = edgeDir;
                float3 up = edge.normal;
                float3 right = edge.right;//normalize(cross(forward, up));

                // Modulate thickness based on length
                float edgeThickness = min(0.5f, max(0.2, edgeLength * 0.3f));
                

                float3 worldPos = edge.positionA
                + v.vertex.z * forward * edgeLength
                + v.vertex.x * right * edgeThickness
                + v.vertex.y * up;
                
                // Transform to clip space
                o.vertex = TransformWorldToHClip(worldPos.xyz);
                o.worldPos = worldPos.xyz;
                o.worldNormal = edge.normal;
                
                o.color = edge.color;
                
                o.instanceID = instanceID;
                
                return o;
            }

            output frag(v2f i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                output o;
                o.color = float4(i.color, 1);
                o.id = i.instanceID;
                return o;
                //if (_WriteSelectionID > 0) return i.instanceID;
                //return i.color;
            }
            ENDHLSL
        }
    }
}