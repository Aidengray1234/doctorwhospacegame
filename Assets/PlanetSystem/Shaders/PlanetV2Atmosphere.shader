Shader "DoctorWho/PlanetV2Atmosphere"
{
    Properties
    {
        _AtmosphereColor("Atmosphere Color", Color) = (0.18,0.48,0.95,1)
        _SunsetColor("Sunset Color", Color) = (1.0,0.30,0.08,1)
        _Density("Density", Range(0,2)) = 0.82
        _RimPower("Rim Power", Range(0.5,10)) = 3.2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Front
        Pass
        {
            Name "AtmosphereForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _AtmosphereColor;
            float4 _SunsetColor;
            float _Density;
            float _RimPower;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionHCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionHCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = normalize(TransformObjectToWorldNormal(i.normalOS));
                return o;
            }

            half4 frag(Varyings i):SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light light = GetMainLight();
                float rim = pow(saturate(1.0 - abs(dot(n, v))), _RimPower);
                float horizonSun = pow(saturate(1.0 - abs(dot(n, light.direction))), 4.0);
                float day = saturate(dot(n, light.direction) * .5 + .5);
                float3 color = lerp(_AtmosphereColor.rgb * .18, _AtmosphereColor.rgb, day);
                color = lerp(color, _SunsetColor.rgb, horizonSun * (1.0 - day * .45));
                float alpha = saturate(rim * _Density * lerp(.35, 1.0, day));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
