// ============================================================================
//  Legends Alley - Glass
//  Built-in render pipeline, VRChat safe on PC and Quest. One pass, no grab
//  pass, no refraction texture: the look comes from a tinted body, a fresnel
//  rim that thickens the edges like real glass, the world's reflection probe
//  sampled at a soft mip, and a sharp highlight from the main light.
//  Cheap enough to glaze a whole storefront.
// ============================================================================
Shader "LegendsAlley/Glass"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.75, 0.85, 1, 0.12)
        _EdgeColor ("Edge Colour", Color) = (0.7, 0.85, 1, 0.75)
        _EdgePower ("Edge Tightness", Range(0.5, 8)) = 3
        _Reflection ("Reflection Strength", Range(0, 1)) = 0.5
        _Blur ("Reflection Blur", Range(0, 7)) = 2
        _Gloss ("Highlight Sharpness", Range(8, 512)) = 160
        _Highlight ("Highlight Strength", Range(0, 2)) = 0.8
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (Off for double sided)", Float) = 2
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull [_Cull]

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            half4 _Tint;
            half4 _EdgeColor;
            half _EdgePower;
            half _Reflection;
            half _Blur;
            half _Gloss;
            half _Highlight;
            half4 _LightColor0;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 world : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                float3 n = normalize(i.normal) * (facing > 0 ? 1 : -1);
                float3 v = normalize(_WorldSpaceCameraPos - i.world);
                half ndv = saturate(dot(n, v));
                half fres = pow(1.0 - ndv, _EdgePower);

                float3 r = reflect(-v, n);
                half3 probe = DecodeHDR(UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, r, _Blur), unity_SpecCube0_HDR);

                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                half spec = pow(saturate(dot(n, normalize(l + v))), _Gloss) * _Highlight;

                half alpha = saturate(_Tint.a + fres * _EdgeColor.a);
                // premultiplied: the body is tinted by alpha, reflections and
                // highlights add on top so glass stays bright where it catches light
                half3 col = _Tint.rgb * _Tint.a + _EdgeColor.rgb * fres * _EdgeColor.a;
                col += probe * _Reflection * (0.25 + 0.75 * fres);
                col += _LightColor0.rgb * spec;
                half4 result = half4(col, alpha);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, result, half4(0, 0, 0, 0));
                return result;
            }
            ENDCG
        }
    }
    Fallback Off
}
