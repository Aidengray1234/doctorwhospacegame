Shader "DoctorWho/PlanetV2Ocean"
{
    Properties
    {
        _ShallowColor("Shallow Color", Color) = (0.03,0.30,0.42,0.72)
        _DeepColor("Deep Color", Color) = (0.005,0.045,0.14,0.88)
        _WaveScale("Wave Scale", Float) = 0.018
        _WaveSpeed("Wave Speed", Float) = 0.18
        _WaveStrength("Wave Strength", Range(0,1)) = 0.28
        _Smoothness("Smoothness", Range(0,1)) = 0.94
        _FresnelPower("Fresnel Power", Range(0.5,8)) = 4.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            Name "OceanForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _ShallowColor;
            float4 _DeepColor;
            float _WaveScale;
            float _WaveSpeed;
            float _WaveStrength;
            float _Smoothness;
            float _FresnelPower;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionHCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float4 shadowCoord:TEXCOORD2; };

            float wave(float3 p)
            {
                float t = _Time.y * _WaveSpeed;
                float a = sin((p.x + p.z) * _WaveScale + t);
                float b = sin((p.z * .73 - p.x * .51) * _WaveScale * 1.61 - t * 1.37);
                float c = sin((p.x * .21 + p.y * .64 + p.z * .37) * _WaveScale * 2.4 + t * .71);
                return (a + b * .6 + c * .35) / 1.95;
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 normalOS = normalize(i.positionOS.xyz);
                float displacement = wave(TransformObjectToWorld(i.positionOS.xyz)) * _WaveStrength;
                float3 displaced = i.positionOS.xyz + normalOS * displacement;
                VertexPositionInputs pos = GetVertexPositionInputs(displaced);
                o.positionHCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = normalize(TransformObjectToWorldNormal(i.normalOS));
                o.shadowCoord = GetShadowCoord(pos);
                return o;
            }

            half4 frag(Varyings i):SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light light = GetMainLight(i.shadowCoord);
                float fresnel = pow(1.0 - saturate(dot(n, v)), _FresnelPower);
                float depthHint = saturate(dot(n, normalize(i.positionWS)) * .5 + .5);
                float3 water = lerp(_DeepColor.rgb, _ShallowColor.rgb, depthHint * .35 + fresnel * .22);
                float3 h = normalize(light.direction + v);
                float specular = pow(saturate(dot(n, h)), lerp(48.0, 320.0, _Smoothness));
                float diffuse = saturate(dot(n, light.direction)) * .22 + .16;
                float3 skyTint = lerp(float3(.02,.12,.22), float3(.30,.55,.72), fresnel);
                float3 color = water * (SampleSH(n) + diffuse * light.color) + skyTint * fresnel * .65;
                color += light.color * specular * light.shadowAttenuation * 1.4;
                float alpha = lerp(_ShallowColor.a, _DeepColor.a, .45) + fresnel * .16;
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
