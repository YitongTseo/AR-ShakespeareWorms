Shader "Burrow/Panel"
{
    Properties { _Color ("Color", Color) = (0, 0, 0, 0.8) }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float2 d = abs(i.uv - 0.5) * 2.0;
                float edge = smoothstep(1.0, 0.96, max(d.x, d.y));   // rounded-ish soft border
                return fixed4(_Color.rgb, _Color.a * edge);
            }
            ENDCG
        }
    }
}
