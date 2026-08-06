Shader "DoctorWho/Voxel Universe Far Masked"
{
    Properties
    {
        _BaseColor("Tint", Color) = (1,1,1,1)
        _Ambient("Ambient", Range(0,1)) = 0.24
        _ObserverPosition("Observer Position", Vector) = (0,0,0,0)
        _HideRadius("Local Hide Radius", Float) = 0
        _FadeWidth("Mask Fade Width", Float) = 1
        _MaskEnabled("Mask Enabled", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry-20"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float _Ambient;
        float4 _ObserverPosition;
        float _HideRadius;
        float _FadeWidth;
        float _MaskEnabled;
        CBUFFER_END

        float VoxelHash(float2 pixel)
        {
            float3 p3 = frac(float3(pixel.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        void ApplyLocalMask(float3 positionWS, float4 positionHCS)
        {
            if (_MaskEnabled < 0.5 || _HideRadius <= 0.001)
                return;

            float distanceToObserver = distance(positionWS, _ObserverPosition.xyz);
            float fade = saturate((distanceToObserver - _HideRadius)
                                  / max(_FadeWidth, 0.001));
            clip(fade - VoxelHash(floor(positionHCS.xy)));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
                float4 shadowCoord : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.shadowCoord = GetShadowCoord(positionInputs);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                ApplyLocalMask(input.positionWS, input.positionHCS);
                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(input.shadowCoord);
                float diffuse = saturate(dot(normalWS, mainLight.direction));
                float3 ambient = SampleSH(normalWS) + _Ambient;
                float3 lighting = ambient
                                  + mainLight.color * diffuse * mainLight.shadowAttenuation;
                half3 albedo = _BaseColor.rgb * input.color.rgb;
                return half4(albedo * lighting, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            DepthVaryings DepthVertex(DepthAttributes input)
            {
                DepthVaryings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            half4 DepthFragment(DepthVaryings input) : SV_Target
            {
                ApplyLocalMask(input.positionWS, input.positionHCS);
                return 0;
            }
            ENDHLSL
        }
    }
}
