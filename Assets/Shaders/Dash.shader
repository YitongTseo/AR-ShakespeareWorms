Shader "Burrow/Dash"
{
    // Vertex-coloured strokes for the sense overlays. uv.x is the distance along the stroke in
    // metres: dashes of _Period that drift toward uv.x = 0 at _Flow m/s (_Period 0 = solid).
    // uv.y runs 0..1 across the stroke for soft edges; a constant 0.5 gives a hard-edged fill,
    // which is how the chemosensory bars use it.
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Period ("Dash period (m)", Float) = 0.008
        _Flow ("Flow (m/s)", Float) = 0.01
        [Enum(UnityEngine.Rendering.BlendMode)] _Dst ("Dst blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+8" }
        Blend SrcAlpha [_Dst]
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color; float _Period, _Flow;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color * _Color; o.uv = v.uv; return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = i.color;
                c.a *= smoothstep(0.0, 0.5, 1.0 - abs(i.uv.y - 0.5) * 2.0);
                if (_Period > 0.0)
                {
                    float f = frac((i.uv.x + _Time.y * _Flow) / _Period);
                    c.a *= smoothstep(0.0, 0.1, f) * smoothstep(0.55, 0.45, f);
                }
                return c;
            }
            ENDCG
        }
    }
}
