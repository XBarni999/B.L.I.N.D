Shader "Hidden/BLIND/Thermal"
{
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _BlindDetailTex ("Surface", 2D) = "white" {}
    }
    SubShader
    {
        HLSLINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _BlindDetailTex;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float4 _MainTex_TexelSize, _BlindDetailTex_ST;
        float4 _BlindHeatSources[8];
        float4 _BlindHeatPowers[8];
        float _BlindBodyHeat, _BlindDetailAmount, _BlindCutoff, _BlindUseAlpha, _BlindEffect;
        float _BlindAdditiveShape;
        float _BlindMode, _BlindSpan, _BlindNoise, _BlindAtmosphere, _BlindWhiteHotCeiling;
        struct Attributes { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; };
        struct Varyings { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; float3 normal:TEXCOORD2; float4 screen:TEXCOORD3; float eye:TEXCOORD4; float4 color:COLOR; };
        Varyings HeatVertex(Attributes i) {
            Varyings o;
            o.position = UnityObjectToClipPos(i.vertex);
            o.world = mul(unity_ObjectToWorld, i.vertex).xyz;
            o.normal = UnityObjectToWorldNormal(i.normal);
            o.uv = i.uv * _BlindDetailTex_ST.xy + _BlindDetailTex_ST.zw;
            o.screen = ComputeScreenPos(o.position);
            o.eye = -UnityObjectToViewPos(i.vertex).z;
            o.color = i.color;
            return o;
        }
        float HeatFragment(Varyings i):SV_Target {
            float2 screenUV = i.screen.xy / i.screen.w;
            float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenUV);
            float sceneEye = LinearEyeDepth(raw);
            float depthTol = min(0.35,max(0.04, i.eye * 0.000015));
            clip(sceneEye + depthTol - i.eye);
            if (_BlindEffect > 1.5) {
                float2 p = i.uv * 2 - 1;
                float r2 = dot(p,p);
                clip(1-r2);
                float signal = 0.13 + max(0,_BlindBodyHeat-0.13) * exp2(-r2*4);
                return lerp(0.13,signal,exp(-i.eye*(0.000016+_BlindAtmosphere*0.00010)));
            }
            float4 detail = tex2D(_BlindDetailTex, i.uv);
            if (_BlindEffect > 0.5) {
                // Smooth shape calculation respecting sprite alpha and RGB intensity
                float shape = lerp(detail.a, max(detail.r, max(detail.g, detail.b)), _BlindAdditiveShape);
                float fade = saturate((sceneEye - i.eye) / 1.2);
                float alpha = saturate(shape * i.color.a) * fade;
                clip(alpha - 0.003);

                // Modulate heat signature by particle color luminosity:
                // Burning fireball cores (bright yellow/orange/white) radiate maximum thermal energy,
                // while cooling smoke edges blend softly into ambient temperature.
                float particleLum = dot(i.color.rgb, float3(0.299, 0.587, 0.114));
                float coreMod = lerp(0.35, 1.25, saturate(particleLum));
                
                // Soft edge falloff instead of flat billboard disk
                float softAlpha = smoothstep(0.003, 0.45, alpha);
                float signal = max(0, _BlindBodyHeat) * coreMod * softAlpha;
                return signal * exp(-i.eye * (0.000016 + _BlindAtmosphere * 0.00010));
            }
            if (_BlindUseAlpha > 0.5) clip(detail.a - _BlindCutoff);
            float heat = _BlindBodyHeat;
            [unroll] for (int n=0; n<8; n++) {
                float3 delta = i.world - _BlindHeatSources[n].xyz;
                float r = max(_BlindHeatSources[n].w, 0.05);
                heat += _BlindHeatPowers[n].x * exp2(-dot(delta,delta) / (r*r) * 2.5);
            }
            // Rich mechanical and panel microstructure from texture
            float textureDetail = dot(detail.rgb, float3(0.2126,0.7152,0.0722));
            float3 safeNormal = i.normal * rsqrt(max(dot(i.normal,i.normal),0.0001));
            float facing = abs(dot(safeNormal, normalize(_WorldSpaceCameraPos-i.world)));
            // Top surfaces receive solar and sky radiation, underside remains cooler; geometric facets stand out in FLIR
            float solarSky = safeNormal.y * 0.12;
            heat *= 1.0 + (textureDetail-0.5)*_BlindDetailAmount + (facing-0.5)*0.10 + solarSky;
            float transmission = exp(-i.eye * (0.000016 + _BlindAtmosphere*0.00010));
            return lerp(0.085, max(0.02,heat), transmission);
        }
        float Background(v2f_img i):SV_Target {
            float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
            float distance = LinearEyeDepth(depth);
            bool isSky = distance > _ProjectionParams.z * 0.98;
            if (isSky) {
                // Sky is cold deep space/atmosphere with subtle natural horizon warmth
                float skyCold = 0.025 + 0.015 * saturate(1.0 - i.uv.y);
                return skyCold;
            }
            float3 sceneColor = tex2D(_MainTex, i.uv).rgb;
            float lum = dot(sceneColor, float3(0.2126, 0.7152, 0.0722));
            // Filmic rational compression: prevents sunlit concrete runways, bridge decks and buildings from blinding the FLIR sensor
            float compressedLum = lum / (1.0 + lum * 3.2);
            float terrainHeat = 0.045 + compressedLum * 0.11;

            // Authentic vanilla explosion response: intense visual fireballs and flashes (HDR lum > 0.65)
            // contribute rich, hot radiant heat without artificial sprite cutout artifacts.
            float explosionBloom = saturate((lum - 0.65) * 1.8);
            terrainHeat += explosionBloom * 1.85;

            // Distant terrain fades toward ambient atmospheric temperature while preserving horizon contrast against the cold sky
            return lerp(0.065, terrainHeat, exp(-distance * (0.000018 + _BlindAtmosphere * 0.00010)));
        }
        float3 Iron(float t) {
            float3 a=float3(0.012,0.006,0.022), b=float3(0.18,0.020,0.30);
            float3 c=float3(0.60,0.050,0.28), d=float3(0.96,0.30,0.020);
            float3 e=float3(1,0.78,0.17), f=float3(1,0.985,0.89);
            if(t<0.26) return lerp(a,b,t/0.26);
            if(t<0.48) return lerp(b,c,(t-0.26)/0.22);
            if(t<0.70) return lerp(c,d,(t-0.48)/0.22);
            if(t<0.88) return lerp(d,e,(t-0.70)/0.18);
            return lerp(e,f,(t-0.88)/0.12);
        }
        float4 Palette(v2f_img i):SV_Target {
            float heat = tex2D(_MainTex, i.uv).r;

            // Tactical Digital Detail Enhancement (DDE): unsharp high frequencies bring out vehicle edges and terrain contours
            float2 off = _MainTex_TexelSize.xy * 0.8;
            float localAvg = (tex2D(_MainTex, i.uv + float2(off.x, 0)).r +
                              tex2D(_MainTex, i.uv - float2(off.x, 0)).r +
                              tex2D(_MainTex, i.uv + float2(0, off.y)).r +
                              tex2D(_MainTex, i.uv - float2(0, off.y)).r) * 0.25;
            float edge = heat - localAvg;
            float enhancedHeat = max(0.01, heat + edge * 0.32);

            // Normalized thermal signal mapping: background sits below 0.15, military targets start at 0.50+
            float span = max(0.2, _BlindSpan);
            float t = saturate(log2(1 + max(0, enhancedHeat - 0.035) * 3.2) / log2(1 + span * 3.2));

            // Authentic electro-optical sensor detector noise and subtle MFD scan raster
            float noise = frac(sin(dot(floor(i.uv*_MainTex_TexelSize.zw), float2(12.9898,78.233)) + floor(_Time.y*24))*43758.5453) - 0.5;
            float scanRaster = (fmod(floor(i.uv.y * _MainTex_TexelSize.w), 2.0) - 0.5) * 0.010;
            t = saturate(t + noise * _BlindNoise + scanRaster);

            // White Hot: clear target separation from background; cold background/buildings remain dark gray (0.05-0.12), military targets stand out bright (0.55+), engine cores glow brilliantly
            float whiteNormalized = saturate((enhancedHeat - 0.045) / (span * 0.90));
            float whiteCurve = saturate(pow(whiteNormalized, 0.85) * 0.95 + whiteNormalized * 0.05);
            float whiteHighlight = 1.0 - exp(-max(0, enhancedHeat - 0.045) / span * 2.2);
            float whiteHot = _BlindWhiteHotCeiling * saturate(lerp(whiteCurve, whiteHighlight, saturate((enhancedHeat - 0.40) / 0.7)) + noise * _BlindNoise);

            // Black Hot: clean bright terrain/buildings with visible relief, military targets appear as deep dark silhouettes with pitch black hot engine cores
            float blackHot = saturate(lerp(0.85, 0.02, pow(saturate((enhancedHeat - 0.040) / (span * 0.90)), 0.85)) - noise * _BlindNoise);

            float3 rgb = _BlindMode < 0.5 ? Iron(t) : (_BlindMode < 1.5 ? whiteHot.xxx : blackHot.xxx);
            // Cockpit display linear space target
            return float4(GammaToLinearSpace(rgb), 1);
        }
        ENDHLSL
        Pass {
            Name "Background"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment Background
            #pragma target 3.5
            ENDHLSL
        }
        Pass {
            Name "SurfaceHeat"
            ZTest LEqual ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex HeatVertex
            #pragma fragment HeatFragment
            #pragma target 3.5
            ENDHLSL
        }
        Pass {
            Name "EffectHeat"
            ZTest LEqual ZWrite Off Cull Off
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma vertex HeatVertex
            #pragma fragment HeatFragment
            #pragma target 3.5
            ENDHLSL
        }
        Pass {
            Name "Palette"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment Palette
            #pragma target 3.5
            ENDHLSL
        }
    }
    Fallback Off
}
