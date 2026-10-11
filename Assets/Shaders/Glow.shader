Shader "Burrow/Glow"
{
    // Soft additive point sprites: a neuron is a small radial glow, not a pixel.
    Properties { _Color ("Tint", Color) = (1, 1, 1, 1) _Core ("Core whitening", Float) = 0.6 }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" }
        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color; float _Core;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color * _Color; o.uv = v.uv; return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float d = length(i.uv - 0.5) * 2.0;
                float core = saturate(1.0 - d * 2.2);                 // bright centre
                float halo = exp(-d * d * 3.0) * 0.55;                 // soft skirt
                fixed4 c = i.color; c.a *= saturate(core + halo);
                c.rgb += core * _Core;
                return c;
            }
            ENDCG
        }
    }
}
