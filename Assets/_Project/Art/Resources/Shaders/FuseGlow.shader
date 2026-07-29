Shader "HollowLines/FuseGlow"
{
    // Additive radial glow for the bomb-fuse telegraph (VfxManager). The colour and alpha are
    // driven per-renderer from C# (SpriteRenderer.color) so one shared material animates every
    // armed bomb; the shader only shapes a flat sprite quad into a soft round pulse of light.
    // Loaded via Resources.Load<Shader>("Shaders/FuseGlow"); if it is ever missing, VfxManager
    // falls back to a plain tinted sprite, so this never becomes a hard dependency.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Softness ("Edge Softness", Range(0.05, 1)) = 0.55
        _Core     ("Core Boost",    Range(0, 3))    = 1.3
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
        Blend SrcAlpha One   // additive — the glow reads as emitted light, not a painted square

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
                float4 vertex : SV_POSITION;
                fixed4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            float _Softness;
            float _Core;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color  = v.color;
                o.uv     = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Radial falloff from the sprite centre → a soft round glow instead of a hard tile.
                float2 d    = i.uv - 0.5;
                float  dist = length(d) * 2.0;                 // 0 at centre → ~1 at the edge
                float  glow = saturate(1.0 - dist / _Softness);
                glow        = glow * glow;
                float  core = glow * glow * glow * _Core;       // brighter hotspot in the middle

                fixed4 c = i.color;                             // colour + alpha come from SpriteRenderer.color
                c.a *= saturate(glow + core);
                return c;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
