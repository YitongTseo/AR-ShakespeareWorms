Shader "Burrow/Translucent"
{
    // Vertex-coloured, alpha blended, two-sided: the worm's skin, thin enough to see the neurons inside.
    Properties { _Color ("Tint", Color) = (1, 1, 1, 1) _Solid ("Solidity", Float) = 1 }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color; float _Solid;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float3 wnrm : TEXCOORD0; float3 wpos : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color * _Color;
                o.wnrm = UnityObjectToWorldNormal(v.normal); o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz; return o;
            }
            fixed4 frag (v2f i, float facing : VFACE) : SV_Target
            {
                float3 n = normalize(i.wnrm) * (facing > 0 ? 1 : -1);
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                float rim = pow(1 - saturate(dot(n, V)), 2.0);
                fixed4 c = i.color;
                c.rgb *= 0.75 + 0.45 * rim;
                c.a = saturate(c.a * (0.28 + 0.6 * rim) * _Solid);   // see-through in the middle, solid at the edges
                return c;
            }
            ENDCG
        }
    }
}
