Shader "RainbowHairFlow/Strand Rainbow"
{
    Properties
    {
        _MaskTex ("Hair alpha mask", 2D) = "white" {}
        _SeparateMask ("Separate alpha mask (red)", 2D) = "white" {}
        _UseAlpha ("Use hair transparency", Float) = 1
        _BaseAlpha ("Source opacity", Float) = 1
        _MaskMode ("Alpha mask mode 0-4", Float) = 0
        _MaskScale ("Alpha mask scale", Float) = 1
        _MaskValue ("Alpha mask offset", Float) = 0
        _AlphaCutoff ("Alpha cutoff", Range(0,1)) = 0.01
        _Speed ("Speed", Range(0.1, 3)) = 1
        _Width ("Width", Range(0.2, 2)) = 1
        _Brightness ("Brightness", Range(0.1, 2)) = 1
        [Toggle] _FlowEnabled ("Flow enabled", Float) = 0
        [HideInInspector] _PreviewTime ("Preview time", Float) = -1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-40" }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MaskTex, _SeparateMask; float4 _MaskTex_ST, _SeparateMask_ST; float _AlphaCutoff, _UseAlpha, _BaseAlpha, _MaskMode, _MaskScale, _MaskValue;
            float _PreviewTime;
            float _FlowEnabled; float _Speed, _Width, _Brightness;

            struct Input
            {
                float4 vertex : POSITION; float2 uv : TEXCOORD0;
                float3 path : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varying
            {
                float4 position : SV_POSITION;
                float3 path : TEXCOORD0; float2 uv : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varying Vert(Input input)
            {
                Varying output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(Varying, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.path = input.path; output.uv = TRANSFORM_TEX(input.uv, _MaskTex);
                return output;
            }

            float3 SmoothRainbow(float progress)
            {
                float hue = radians(24 + saturate(progress) * 280);
                float lightness = 0.82;
                float chroma = 0.128;
                float a = cos(hue) * chroma;
                float b = sin(hue) * chroma;
                float3 lms = lightness + float3(
                    0.3963377774 * a + 0.2158037573 * b,
                   -0.1055613458 * a - 0.0638541728 * b,
                   -0.0894841775 * a - 1.2914855480 * b);
                lms *= lms * lms;
                float3 linearRgb = saturate(float3(
                    dot(lms, float3( 4.0767416621, -3.3077115913,  0.2309699292)),
                    dot(lms, float3(-1.2684380046,  2.6097574011, -0.3413193965)),
                    dot(lms, float3(-0.0041960863, -0.7034186147,  1.7076147010))));
                #if defined(UNITY_COLORSPACE_GAMMA)
                return LinearToGammaSpace(linearRgb);
                #else
                return linearRgb;
                #endif
            }
            float4 Frag(Varying input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(_FlowEnabled - 0.5);
                float surfaceAlpha = tex2D(_MaskTex, input.uv).a * _BaseAlpha;
                float mask = saturate(tex2D(_SeparateMask, input.uv * _SeparateMask_ST.xy + _SeparateMask_ST.zw).r * _MaskScale + _MaskValue);
                if (_MaskMode == 1) surfaceAlpha = mask;
                if (_MaskMode == 2) surfaceAlpha *= mask;
                if (_MaskMode == 3) surfaceAlpha = saturate(surfaceAlpha + mask);
                if (_MaskMode == 4) surfaceAlpha = saturate(surfaceAlpha - mask);
                surfaceAlpha = lerp(1, surfaceAlpha, _UseAlpha);
                clip(surfaceAlpha - _AlphaCutoff);
                float seconds = _PreviewTime >= 0 ? _PreviewTime : _Time.y;
                seconds *= _Speed;
                float round = floor(seconds / 1.9);
                float elapsed = seconds - round * 1.9;
                float strand = floor(input.path.z + 0.5);
                float zone = floor(strand / 8);
                float local = strand - zone * 8;
                float pair = floor(local / 2);
                float side = local - pair * 2;
                float seed = round + zone * 29;
                float selected = floor(frac(sin(seed * 12.9898 + 0.91) * 43758.5453) * 4);
                float second = fmod(selected + 1 + floor(frac(sin(seed * 18.27 + 4.5) * 10429.5) * 3), 4);
                float firstPair = 1 - step(0.1, abs(pair - selected));
                float secondPair = 1 - step(0.1, abs(pair - second));
                float firstSide = step(0.5, frac(sin(seed * 43.217 + 1.7) * 23421.631));
                float delay = 0.05 * zone + 0.10 * abs(side - firstSide) + 0.28 * secondPair;
                float lead = elapsed * 1.33 - delay - 0.12;
                float length = zone == 0 ? 0.44 : 0.36;
                float along = (lead - input.path.x) / length;
                float band = smoothstep(-0.035, 0.09, along) * (1 - smoothstep(0.84, 1.04, along));
                float width = zone == 2 ? (pair == 0 ? 0.70 : 0.30) : 1.0;
                float narrow = 1 - smoothstep(0.13 * width * _Width, 0.34 * width * _Width, abs(input.path.y - 0.5));
                float ends = smoothstep(0, 0.035, input.path.x) * (1 - smoothstep(0.95, 1, input.path.x));

                float phase = saturate(along) * 5;
                float3 red = float3(1.00, 0.19, 0.29);
                float3 orange = float3(1.00, 0.49, 0.12);
                float3 yellow = float3(1.00, 0.91, 0.28);
                float3 green = float3(0.24, 0.94, 0.48);
                float3 blue = float3(0.19, 0.64, 1.00);
                float3 violet = float3(0.63, 0.31, 1.00);
                float blend = smoothstep(0, 1, frac(phase));
                float3 rgb = phase < 1 ? lerp(red, orange, blend)
                           : phase < 2 ? lerp(orange, yellow, blend)
                           : phase < 3 ? lerp(yellow, green, blend)
                           : phase < 4 ? lerp(green, blue, blend)
                           : phase < 5 ? lerp(blue, violet, blend)
                                       : violet;
                if (zone == 2 && pair != 0) rgb = SmoothRainbow(along);
                float alpha = saturate((firstPair + secondPair) * 1.28 * band * narrow * ends);
                return float4(rgb * _Brightness, alpha * surfaceAlpha);
            }
            ENDCG
        }
    }
}









