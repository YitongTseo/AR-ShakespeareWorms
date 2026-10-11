Shader "Burrow/Dome"
{
    // The clear dome over each mind: a thin glass shell, reflection + fresnel, no refraction.
    Properties
    {
        _Tint     ("Tint", Color) = (0.85, 0.92, 1.0, 1)
        _KeyDir   ("Key light dir", Vector) = (0.35, 0.75, -0.55, 0)
        _SkyColor ("Sky", Color) = (0.62, 0.66, 0.74, 1)
        _GroundColor ("Floor", Color) = (0.16, 0.15, 0.14, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Tint, _SkyColor, _GroundColor; float4 _KeyDir;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 wnrm : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wnrm = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.wnrm), V = normalize(_WorldSpaceCameraPos - i.wpos);
                float fres = pow(1 - saturate(dot(n, V)), 3.5);
                float3 R = reflect(-V, n);
                float3 e = lerp(_GroundColor.rgb, _SkyColor.rgb, saturate(R.y * 0.7 + 0.5));
                float3 k = normalize(_KeyDir.xyz);
                float hi = pow(saturate(dot(R, k)), 60.0);
                float3 col = _Tint.rgb * e * (0.25 + fres) + hi * 1.2;
                float alpha = saturate(0.12 + fres * 0.8 + hi);
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}
