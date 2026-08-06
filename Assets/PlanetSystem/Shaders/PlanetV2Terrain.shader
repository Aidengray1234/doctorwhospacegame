Shader "DoctorWho/PlanetV2Terrain"
{
    Properties
    {
        _DetailScale("Detail Scale", Float) = 0.08
        _DetailStrength("Detail Strength", Range(0,0.35)) = 0.12
        _Smoothness("Smoothness", Range(0,1)) = 0.18
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _DetailScale;
            float _DetailStrength;
            float _Smoothness;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 color:COLOR; };
            struct Varyings { float4 positionHCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float4 color:COLOR; float4 shadowCoord:TEXCOORD2; };

            float hash31(float3 p)
            {
                p = frac(p * .1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float valueNoise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f*f*(3.0-2.0*f);
                float n000 = hash31(i + float3(0,0,0));
                float n100 = hash31(i + float3(1,0,0));
                float n010 = hash31(i + float3(0,1,0));
                float n110 = hash31(i + float3(1,1,0));
                float n001 = hash31(i + float3(0,0,1));
                float n101 = hash31(i + float3(1,0,1));
                float n011 = hash31(i + float3(0,1,1));
                float n111 = hash31(i + float3(1,1,1));
                return lerp(lerp(lerp(n000,n100,f.x),lerp(n010,n110,f.x),f.y),lerp(lerp(n001,n101,f.x),lerp(n011,n111,f.x),f.y),f.z);
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionHCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color;
                o.shadowCoord = GetShadowCoord(pos);
                return o;
            }

            half4 frag(Varyings i):SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 weights = pow(abs(n), 5.0);
                weights /= max(.0001, weights.x + weights.y + weights.z);
                float detail = valueNoise(i.positionWS * _DetailScale);
                float triplanar = detail * weights.x + valueNoise(i.positionWS.yzx * _DetailScale) * weights.y + valueNoise(i.positionWS.zxy * _DetailScale) * weights.z;
                float3 albedo = saturate(i.color.rgb * lerp(1.0 - _DetailStrength, 1.0 + _DetailStrength, triplanar));
                Light mainLight = GetMainLight(i.shadowCoord);
                float ndl = saturate(dot(n, mainLight.direction));
                float3 diffuse = albedo * (SampleSH(n) + mainLight.color * ndl * mainLight.shadowAttenuation);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 halfDir = normalize(mainLight.direction + viewDir);
                float spec = pow(saturate(dot(n, halfDir)), lerp(8.0, 96.0, _Smoothness)) * _Smoothness;
                return half4(diffuse + mainLight.color * spec * mainLight.shadowAttenuation, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
