Shader "HollowLines/ExitGlow"
{
    // Ambient floor glow for the campaign/tutorial exit zone (BoardView) — the last few rows
    // before the win threshold breathe with warm light, rendered ON TOP of the tile layer
    // (sortingOrder above the tiles' default 0) with an additive blend, so it washes the whole
    // exit zone in warm light without hiding the block colors underneath. Per-row brightness comes
    // from SpriteRenderer.color.a (set once in BoardView.Init, brighter toward the bottom row); the
    // shader only adds the animated pulse on top, so one shared material serves every glow row.
    // Loaded via Resources.Load<Shader>("Shaders/ExitGlow"); if missing, BoardView falls back to a
    // plain low-alpha tinted strip (same contract as FuseGlow / DiamondShine).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _PulseSpeed    ("Pulse Speed",    Range(0.2, 4)) = 0.9
        _PulseStrength ("Pulse Strength", Range(0, 1))   = 0.35
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
        Blend SrcAlpha One   // additive — reads as light washing over the tiles, never darkens or hides one

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

            float _PulseSpeed;
            float _PulseStrength;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.color    = v.color;
                o.uv       = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // Per-row phase offset (from world Y) so the glow rows don't all pulse in lockstep.
            float Hash(float p)
            {
                return frac(sin(p * 12.9898) * 43758.5453);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float seed  = Hash(floor(i.worldPos.y + 0.001));
                float pulse = (sin(_Time.y * _PulseSpeed + seed * 6.2831853) * 0.5 + 0.5) * _PulseStrength;

                fixed4 c = i.color;              // color + alpha come from SpriteRenderer.color
                c.a *= saturate(0.65 + pulse);
                return c;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
