Shader "HollowLines/DiamondShine"
{
    // R4: a persistent shimmer for undrilled Diamond tiles — a soft pulsing glow plus a diagonal
    // glint sweep, both offset per-tile by a hash of world position so a whole board of diamonds
    // doesn't twinkle in lockstep. Unlike FuseGlow (an additive halo layered OVER a bomb's own
    // sprite), this shader IS the diamond tile's own material: it alpha-blends normally against
    // the board so it reads as solid content, and brightens its own RGB for the shine instead of
    // relying on a second renderer. BoardView assigns this material only to Diamond cells and
    // falls back to the default sprite material everywhere else (including a drilled diamond).
    // Loaded via Resources.Load<Shader>("Shaders/DiamondShine"); if missing, BoardView falls back
    // to the plain tinted tile, so this never becomes a hard dependency (same contract as FuseGlow).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _ShineColor    ("Shine Color",    Color) = (1, 1, 1, 1)
        _PulseSpeed    ("Pulse Speed",    Range(0.2, 6))   = 1.6
        _PulseStrength ("Pulse Strength", Range(0, 1))     = 0.30
        _SweepSpeed    ("Sweep Speed",    Range(0.05, 3))  = 0.5
        _SweepWidth    ("Sweep Width",    Range(0.02, 0.6)) = 0.14
        _SweepStrength ("Sweep Strength", Range(0, 2))     = 0.9
    }

    SubShader
    {
        Tags
        {
            "Queue"            = "Transparent"
            "RenderType"       = "Transparent"
            "IgnoreProjector"  = "True"
            "PreviewType"      = "Plane"
            "CanUseSpriteAtlas"= "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha   // normal alpha blend — this IS the tile, not an overlay

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _ShineColor;
            float  _PulseSpeed;
            float  _PulseStrength;
            float  _SweepSpeed;
            float  _SweepWidth;
            float  _SweepStrength;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.color    = v.color;
                o.uv       = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // Cheap hash so every diamond tile twinkles on its own phase without a MaterialPropertyBlock.
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed4 baseCol = tex * i.color;

                float seed  = Hash(floor(i.worldPos.xy + 0.001));
                float phase = _Time.y * _PulseSpeed + seed * 6.2831853;
                float pulse = (sin(phase) * 0.5 + 0.5) * _PulseStrength;

                // Diagonal glint that sweeps across the gem, offset per-tile by the same seed.
                float diag  = i.uv.x + i.uv.y - (_Time.y * _SweepSpeed + seed * 4.0);
                float sweep = 1.0 - saturate(abs(frac(diag) - 0.5) / _SweepWidth);
                sweep = sweep * sweep * _SweepStrength;

                fixed3 glow = _ShineColor.rgb * (pulse + sweep) * baseCol.a;

                fixed4 outCol;
                outCol.rgb = baseCol.rgb + glow;
                outCol.a   = baseCol.a;
                return outCol;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
