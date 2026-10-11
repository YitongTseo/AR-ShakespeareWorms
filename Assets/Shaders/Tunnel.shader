Shader "Burrow/Tunnel"
{
    // The burrow network. One static baked mesh; all motion is here in the vertex stage:
    //   breathing  : slow radius swell per run (phase in _Runs[run].w)
    //   boluses    : a gaussian bulge travelling along a run, with a slight constriction
    //                behind it -- the anaconda swallowing its prey (_Runs[run].xyz)
    //   sway       : blind spur tips drift
    // Per-vertex data baked by tools/burrow/field.py:
    //   uv0 = (s metres along the run, tube radius, phi around the tube, node proximity)
    //   uv1 = (run id, tip weight)
    // Surface: metallic, with a procedural wound-wire normal from (s, phi) like the real sculpture.
    Properties
    {
        _BaseColor   ("Metal colour", Color) = (0.70, 0.71, 0.74, 1)
        _Rough       ("Roughness", Range(0.05, 1)) = 0.34
        _WrapPitch   ("Wire pitch (m)", Float) = 0.0048
        _WrapDepth   ("Wire depth", Float) = 0.0016
        _BreathAmp   ("Breath amplitude", Range(0, 0.3)) = 0.11
        _BreathSpeed ("Breath speed", Float) = 0.6
        _SwayAmp     ("Tip sway (m)", Float) = 0.05
        _RunSway     ("Run sway (m)", Float) = 0.035
        _RunSwaySpeed ("Run sway speed", Float) = 0.33
        _KeyDir      ("Key light dir", Vector) = (0.35, 0.75, -0.55, 0)
        _KeyColor    ("Key light", Color) = (1.0, 0.97, 0.92, 1)
        _SkyColor    ("Sky / ceiling", Color) = (0.62, 0.66, 0.74, 1)
        _GroundColor ("Floor", Color) = (0.16, 0.15, 0.14, 1)
        _Exposure    ("Exposure", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            #define MAX_RUNS 512
            float4 _Runs[MAX_RUNS];   // x bolus s, y bolus amp (x radius), z bolus half width, w breath phase
            float4 _RunInfo[MAX_RUNS]; // x run length (m), yzw a unit sway direction per run

            fixed4 _BaseColor, _KeyColor, _SkyColor, _GroundColor;
            float _Rough, _WrapPitch, _WrapDepth, _BreathAmp, _BreathSpeed, _SwayAmp, _RunSway, _RunSwaySpeed, _Exposure;
            float4 _KeyDir;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 uv0    : TEXCOORD0;
                float2 uv1    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos  : SV_POSITION;
                float3 wpos : TEXCOORD0;
                float3 wnrm : TEXCOORD1;
                float4 data : TEXCOORD2;   // s, cos phi, sin phi, prox
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float s = v.uv0.x, r = v.uv0.y, phi = v.uv0.z, prox = v.uv0.w;
                int run = (int)(v.uv1.x + 0.5);
                float tip = v.uv1.y;
                float4 R = _Runs[run];
                float t = _Time.y;

                float breath = sin(t * _BreathSpeed + R.w + s * 2.3) * _BreathAmp;
                float hw = max(R.z, 1e-3);
                float d1 = (s - R.x) / hw;
                float bol = R.y * exp(-d1 * d1);
                float d2 = (s - R.x + 1.7 * hw) / hw;
                bol -= 0.22 * R.y * exp(-d2 * d2);          // constriction behind the lump
                float disp = (breath + bol) * r * (1.0 - 0.75 * prox);

                float3 p = v.vertex.xyz + v.normal * disp;
                // every run sways as a whole: a bow between its two ends (zero at the junctions so
                // the network never tears), slow and out of phase run to run
                float4 I = _RunInfo[run];
                float bow = sin(3.14159 * saturate(s / max(I.x, 0.05)));
                float ph = R.w * 3.0;
                float3 swayDir = normalize(I.yzw + float3(sin(t * 0.21 + ph), 0.6 * sin(t * 0.17 + ph * 1.3), cos(t * 0.19 + ph)) * 0.5);
                p += swayDir * bow * sin(t * _RunSwaySpeed + ph) * _RunSway * (1.0 - 0.6 * prox);
                p += float3(sin(t * 0.37 + R.w), sin(t * 0.29 + R.w * 1.7), cos(t * 0.41 + R.w)) * tip * tip * _SwayAmp;

                o.pos  = UnityObjectToClipPos(float4(p, 1));
                o.wpos = mul(unity_ObjectToWorld, float4(p, 1)).xyz;
                o.wnrm = UnityObjectToWorldNormal(v.normal);
                o.data = float4(s, cos(phi), sin(phi), prox);
                return o;
            }

            float3 env(float3 d)
            {
                float3 c = lerp(_GroundColor.rgb, _SkyColor.rgb, saturate(d.y * 0.7 + 0.5));
                float3 k = normalize(_KeyDir.xyz);
                c += _KeyColor.rgb * pow(saturate(dot(d, k)), 6.0) * 0.9;        // the window
                return c;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.wnrm);
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);

                // --- wound wire: a helix in (s, phi); round wire cross-section
                float phi = atan2(i.data.z, i.data.y);
                float u = i.data.x / _WrapPitch - phi / 6.2831853;
                float f = frac(u) - 0.5;
                float h = sqrt(saturate(0.25 - f * f)) * 2.0;        // 0..1 semicircle per wire
                float aa = saturate(1.6 - fwidth(u) * 2.5);          // fade when a pitch is ~a pixel
                h *= _WrapDepth * aa * (1.0 - i.data.w);              // smooth cast metal at nodules/chambers
                float3 dpdx = ddx(i.wpos), dpdy = ddy(i.wpos);
                float dhdx = ddx(h), dhdy = ddy(h);
                float3 r1 = cross(dpdy, n), r2 = cross(n, dpdx);
                float det = dot(dpdx, r1);
                float3 grad = sign(det) * (dhdx * r1 + dhdy * r2);
                n = normalize(abs(det) * n - grad * 1.0);

                // --- metal: fresnel env reflection + GGX key highlight
                float3 L = normalize(_KeyDir.xyz);
                float3 H = normalize(L + V);
                float NdV = saturate(dot(n, V)), NdL = saturate(dot(n, L)), NdH = saturate(dot(n, H));
                float3 F0 = _BaseColor.rgb;
                float3 F = F0 + (1 - F0) * pow(1 - NdV, 5) * 0.6;
                float a = _Rough * _Rough, a2 = a * a;
                float dd = NdH * NdH * (a2 - 1) + 1;
                float D = a2 / (3.14159 * dd * dd + 1e-4);
                float3 spec = D * F0 * NdL * 0.25 * _KeyColor.rgb;
                float3 R = reflect(-V, n);
                float3 e = env(normalize(lerp(R, n, _Rough * 0.6)));
                float3 col = F * e * (1.0 - 0.35 * _Rough) + spec;
                col += _BaseColor.rgb * 0.04 * (NdL + 0.3);                     // a little diffuse dirt
                col *= _Exposure;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
