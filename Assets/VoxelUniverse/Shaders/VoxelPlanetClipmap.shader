Shader "DoctorWho/Voxel Planet Clipmap"
{
    Properties
    {
        _BaseColor("Tint", Color) = (1,1,1,1)
        _Ambient("Ambient", Range(0,1)) = 0.28
        _ClipMode("Clip Mode", Float) = 0
        _FocusDirectionOS("Focus Direction", Vector) = (0,1,0,0)
        _InnerCos("Inner Cos", Float) = 1.1
        _OuterCos("Outer Cos", Float) = -1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry-10" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _FocusDirectionOS;
            float _Ambient;
            float _ClipMode;
            float _InnerCos;
            float _OuterCos;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 directionOS : TEXCOORD1;
                float4 color : COLOR;
                float4 shadowCoord : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.directionOS = normalize(input.positionOS.xyz);
                output.color = input.color;
                output.shadowCoord = GetShadowCoord(positionInputs);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float focusDot = dot(normalize(input.directionOS), normalize(_FocusDirectionOS.xyz));
                if (_ClipMode > 0.5 && _ClipMode < 1.5)
                {
                    clip(_InnerCos - focusDot);
                    clip(focusDot - _OuterCos);
                }
                else if (_ClipMode >= 1.5)
                {
                    clip(_InnerCos - focusDot);
                }

                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(input.shadowCoord);
                float diffuse = saturate(dot(normalWS, mainLight.direction));
                float3 ambient = SampleSH(normalWS) + _Ambient;
                float3 lighting = ambient
                    + mainLight.color * diffuse * mainLight.shadowAttenuation;
                float3 albedo = input.color.rgb * _BaseColor.rgb;
                return half4(albedo * lighting, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
