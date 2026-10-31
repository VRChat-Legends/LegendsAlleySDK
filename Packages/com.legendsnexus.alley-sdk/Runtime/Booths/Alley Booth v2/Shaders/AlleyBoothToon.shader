// Cel shaded booth surface: two light bands, soft rim, emission mask for the neon.
// Surface shader so baked lightmaps, light probes and the meta pass all work.
Shader "LegendsAlley/Booth Toon"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _EmissionMap ("Glow Mask", 2D) = "black" {}
        [HDR] _EmissionColor ("Glow Color", Color) = (0,0,0,1)
        _ShadowLevel ("Shadow Level", Range(0,1)) = 0.6
        _Threshold ("Band Threshold", Range(0,1)) = 0.55
        _Softness ("Band Softness", Range(0.001,0.2)) = 0.02
        _RimColor ("Rim Color", Color) = (0.1,0.1,0.12,1)
        _RimPower ("Rim Power", Range(1,8)) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Toon fullforwardshadows
        #pragma target 3.0
        #include "UnityPBSLighting.cginc"

        sampler2D _MainTex;
        sampler2D _EmissionMap;
        fixed4 _Color;
        half4 _EmissionColor;
        half _ShadowLevel;
        half _Threshold;
        half _Softness;
        fixed4 _RimColor;
        half _RimPower;

        struct Input
        {
            float2 uv_MainTex;
            float3 viewDir;
        };

        inline half4 LightingToon(SurfaceOutput s, half3 viewDir, UnityGI gi)
        {
            half nl = dot(s.Normal, gi.light.dir) * 0.5 + 0.5;
            half band = smoothstep(_Threshold - _Softness, _Threshold + _Softness, nl);
            half4 c;
            c.rgb = s.Albedo * gi.light.color * lerp(_ShadowLevel, 1, band);
            #ifdef UNITY_LIGHT_FUNCTION_APPLY_INDIRECT
            c.rgb += s.Albedo * gi.indirect.diffuse;
            #endif
            c.a = s.Alpha;
            return c;
        }

        inline void LightingToon_GI(SurfaceOutput s, UnityGIInput data, inout UnityGI gi)
        {
            gi = UnityGlobalIllumination(data, 1.0, s.Normal);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = albedo.rgb;
            o.Alpha = 1;
            half rim = pow(1 - saturate(dot(normalize(IN.viewDir), o.Normal)), _RimPower);
            o.Emission = tex2D(_EmissionMap, IN.uv_MainTex).rgb * _EmissionColor.rgb + _RimColor.rgb * rim;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
