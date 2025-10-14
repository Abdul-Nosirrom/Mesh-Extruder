Shader "Hidden/MeshProfileThumbnail"
{
    Properties {}
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        ZTest Always
        
        Pass
        {
            
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                // Clip pos to screen UVs
                float2 uv = i.pos.xy / i.pos.w * 0.5 + 0.5;
                uv.y = 1 - uv.y;

                // Diagonal stripes
                float stripes = step(0.5, frac((uv.x + uv.y) * 0.3));
                if (stripes < 0.5)
                    return fixed4(0.1, 0.1, 0.3, 1);
                else
                    return fixed4(0.7, 0.7, 0.7, 1);
            }
            ENDCG
        }
    }
}

