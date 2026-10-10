Shader "Solutio/UI/FrostedGlassBlur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint Color", Color) = (0.05, 0.05, 0.08, 0.85)
        _BlurSize ("Blur Size", Range(0, 10)) = 3.0
        _VignetteStrength ("Vignette Strength", Range(0, 1)) = 0.4
        
        // UI Stencil support
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "FrostedGlassBlurPass"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _BlurSize;
            float _VignetteStrength;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float stepU = (_BlurSize * 0.0015);
                float stepV = (_BlurSize * 0.0015);

                // Multi-tap Box/Gaussian distribution sampling
                fixed4 col = fixed4(0, 0, 0, 0);
                col += tex2D(_MainTex, uv + float2(-stepU, -stepV)) * 0.075;
                col += tex2D(_MainTex, uv + float2( 0.0,   -stepV)) * 0.125;
                col += tex2D(_MainTex, uv + float2( stepU, -stepV)) * 0.075;
                col += tex2D(_MainTex, uv + float2(-stepU,  0.0))   * 0.125;
                col += tex2D(_MainTex, uv + float2( 0.0,    0.0))   * 0.200;
                col += tex2D(_MainTex, uv + float2( stepU,  0.0))   * 0.125;
                col += tex2D(_MainTex, uv + float2(-stepU,  stepV)) * 0.075;
                col += tex2D(_MainTex, uv + float2( 0.0,    stepV)) * 0.125;
                col += tex2D(_MainTex, uv + float2( stepU,  stepV)) * 0.075;

                col = (col + _TextureSampleAdd) * IN.color;

                float2 d = uv - float2(0.5, 0.5);
                float vignette = 1.0 - dot(d, d) * _VignetteStrength * 2.0;
                col.rgb *= saturate(vignette);

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                return col;
            }
            ENDCG
        }
    }
}