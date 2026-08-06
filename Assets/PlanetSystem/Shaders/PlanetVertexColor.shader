Shader "DoctorWho/PlanetVertexColor"
{
    Properties { _Smoothness("Smoothness", Range(0,1)) = 0.15 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 color:COLOR; };
            struct Varyings { float4 positionHCS:SV_POSITION; float3 normalWS:TEXCOORD0; float4 color:COLOR; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color;
                return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float ndl = saturate(dot(normalize(i.normalWS), lightDir)) * 0.75 + 0.25;
                return half4(i.color.rgb * ndl, 1);
            }
            ENDHLSL
        }
    }
}
