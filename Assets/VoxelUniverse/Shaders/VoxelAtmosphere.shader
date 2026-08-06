Shader "DoctorWho/Voxel Universe Atmosphere"
{
    Properties
    {
        _AtmosphereColor("Atmosphere Color", Color) = (0.32,0.58,0.92,1)
        _SunsetColor("Sunset Color", Color) = (1,0.32,0.1,1)
        _Density("Density", Range(0.05,4)) = 0.7
        _GroundRadius("Ground Radius", Float) = 256
        _AtmosphereRadius("Atmosphere Radius", Float) = 328
        _PlanetCenter("Planet Center", Vector) = (0,0,0,0)
        _SunDirection("Sun Direction", Vector) = (0.3,0.8,0.2,0)
        _ObserverAltitude01("Observer Altitude", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+40" }
        Pass
        {
            Name "Atmosphere"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _AtmosphereColor;
            float4 _SunsetColor;
            float4 _PlanetCenter;
            float4 _SunDirection;
            float _Density;
            float _GroundRadius;
            float _AtmosphereRadius;
            float _ObserverAltitude01;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 radial = normalize(input.positionWS - _PlanetCenter.xyz);
                float3 cameraRadial = normalize(_WorldSpaceCameraPos - _PlanetCenter.xyz);
                float horizon = pow(saturate(1.0 - abs(dot(radial, cameraRadial))), 2.1);
                float sunFacing = saturate(dot(radial, normalize(_SunDirection.xyz)));
                float sunsetBand = pow(saturate(1.0 - abs(sunFacing)), 7.0);
                float daySide = 0.32 + 0.68 * saturate(sunFacing * 0.65 + 0.45);
                float outsideRim = lerp(0.45, 1.35, _ObserverAltitude01) * horizon;
                float insideFill = (1.0 - _ObserverAltitude01) * (0.10 + 0.24 * horizon);
                float alpha = saturate((outsideRim + insideFill) * _Density * 0.58);
                float3 color = lerp(_AtmosphereColor.rgb, _SunsetColor.rgb,
                    sunsetBand * (0.35 + 0.65 * horizon));
                color *= daySide + horizon * 0.55;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
