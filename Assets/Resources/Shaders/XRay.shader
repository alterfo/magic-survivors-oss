// Силуэт мага сквозь стены: рисуется только там, где его заслонил декор (ZTest Greater), светлый контур по краям.
// Лежит в Resources, чтобы шейдер попал в сборку без отдельного материала.
Shader "MagicSurvivors/XRay"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.8, 0.45, 1)
        _Fill ("Fill", Range(0, 1)) = 0.18
        _Power ("Rim power", Float) = 2.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "XRay"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest Greater
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Fill;
                half _Power;
            CBUFFER_END

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 w = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(w);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(w);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half rim = pow(1 - saturate(dot(normalize(i.normalWS), normalize(i.viewWS))), _Power);
                return half4(_Color.rgb, saturate(_Fill + rim) * _Color.a);
            }
            ENDHLSL
        }
    }
}
