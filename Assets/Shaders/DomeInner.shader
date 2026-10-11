Shader "Burrow/DomeInner"
{
    // The dark thing inside each dome: the mind. Faint constellation of neuron-like specks,
    // brightening to a glow when the mind is selected (_Glow via property block).
    Properties
    {
        _Dark   ("Dark", Color) = (0.01, 0.012, 0.02, 1)
        _GlowColor ("Glow", Color) = (0.35, 0.75, 1.0, 1)
        _Glow   ("Glow", Range(0, 1)) = 0
        _Seed   ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            fixed4 _Dark, _GlowColor;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _Glow)
                UNITY_DEFINE_INSTANCED_PROP(float, _Seed)
            UNITY_INSTANCING_BUFFER_END(Props)
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 onrm : TEXCOORD0; float3 wnrm : TEXCOORD1; float3 wpos : TEXCOORD2;
                         UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_OUTPUT(v2f, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.onrm = v.normal; o.wnrm = UnityObjectToWorldNormal(v.normal);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float glow = UNITY_ACCESS_INSTANCED_PROP(Props, _Glow);
                float seed = UNITY_ACCESS_INSTANCED_PROP(Props, _Seed);
                float3 n = normalize(i.onrm);
                float3 cell = floor(n * 7.0 + seed * 13.0);
                float sp = step(0.86, hash(cell)) * smoothstep(0.55, 0.0, length(frac(n * 7.0 + seed * 13.0) - 0.5));
                float pulse = 0.5 + 0.5 * sin(_Time.y * 2.0 + hash(cell + 1.0) * 6.28);
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                float rim = pow(1 - saturate(dot(normalize(i.wnrm), V)), 2.0);
                float3 col = _Dark.rgb + _GlowColor.rgb * (sp * (0.15 + 0.85 * pulse) * (0.35 + glow) + rim * glow * 0.6 + glow * 0.08);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
