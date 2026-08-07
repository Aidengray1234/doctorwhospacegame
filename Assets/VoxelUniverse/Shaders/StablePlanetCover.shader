Shader "DoctorWho/VoxelUniverse/Stable Planet Cover"
{
    Properties
    {
        _Brightness("Brightness", Range(0.2, 2.0)) = 1.0
        _ObserverDirection("Observer Direction", Vector) = (0,1,0,0)
        _HoleCos("Local Detail Hole Cos", Float) = 1.1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 localDirection : TEXCOORD1;
                half4 color : COLOR;
                half fogFactor : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half _Brightness;
                float4 _ObserverDirection;
                float _HoleCos;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.normalWS = normalInputs.normalWS;
                output.localDirection = normalize(input.positionOS.xyz);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // _HoleCos > 1 means no hole. Otherwise remove only the cap already covered
                // by real worker-built cube chunks. This keeps the horizon filled while
                // preventing the cover from poking through nearby detailed terrain.
                if (_HoleCos <= 1.0)
                {
                    float towardObserver = dot(normalize(input.localDirection),
                        normalize(_ObserverDirection.xyz));
                    clip(_HoleCos - towardObserver);
                }

                Light mainLight = GetMainLight();
                half diffuse = saturate(dot(normalize(input.normalWS), mainLight.direction));
                half3 ambient = SampleSH(normalize(input.normalWS));
                half3 lighting = ambient + mainLight.color * (0.25h + diffuse * 0.75h);
                half3 color = input.color.rgb * lighting * _Brightness;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
