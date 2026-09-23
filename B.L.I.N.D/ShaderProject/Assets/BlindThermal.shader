Shader "Hidden/BLIND/Thermal"
{
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _DetailTex ("Surface", 2D) = "white" {}
    }
    SubShader
    {
        HLSLINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _DetailTex;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float4 _MainTex_TexelSize, _DetailTex_ST;
        float4 _HeatSources[8];
        float4 _HeatPowers[8];
        float _BodyHeat, _DetailAmount, _Cutoff, _UseAlpha, _Effect;
        float _Mode, _Span, _Noise, _Atmosphere, _WhiteHotCeiling;
        struct Attributes { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; };
        struct Varyings { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; float3 normal:TEXCOORD2; float4 screen:TEXCOORD3; float eye:TEXCOORD4; float4 color:COLOR; };
        Varyings HeatVertex(Attributes i) {
            Varyings o;
            o.position = UnityObjectToClipPos(i.vertex);
            o.world = mul(unity_ObjectToWorld, i.vertex).xyz;
            o.normal = UnityObjectToWorldNormal(i.normal);
            o.uv = i.uv * _DetailTex_ST.xy + _DetailTex_ST.zw;
            o.screen = ComputeScreenPos(o.position);
            o.eye = -UnityObjectToViewPos(i.vertex).z;
            o.color = i.color;
            return o;
        }
        float HeatFragment(Varyings i):SV_Target {
            float2 screenUV = i.screen.xy / i.screen.w;
            float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenUV);
            float sceneEye = LinearEyeDepth(raw);
            // Compare in eye-space: generous tolerance for exhaust and explosion fireballs prevents clipping
            float depthTol = _Effect > 1.5 ? max(6.0, i.eye * 0.02) : max(0.25, i.eye * 0.00015);
            clip(sceneEye + depthTol - i.eye);
            if (_Effect > 1.5) {
                float2 p = i.uv * 2 - 1;
                float r2 = dot(p,p);
                clip(1-r2);
                float signal = 0.13 + max(0,_BodyHeat-0.13) * exp2(-r2*4);
                return lerp(0.13,signal,exp(-i.eye*(0.000016+_Atmosphere*0.00010)));
            }
            float4 detail = tex2D(_DetailTex, i.uv);
            if (_UseAlpha > 0.5) clip(detail.a - _Cutoff);
            float heat = _BodyHeat;
            [unroll] for (int n=0; n<8; n++) {
                float3 delta = i.world - _HeatSources[n].xyz;
                float r = max(_HeatSources[n].w, 0.05);
                heat += _HeatPowers[n].x * exp2(-dot(delta,delta) / (r*r) * 2.5);
            }
            // Weak microstructure only: camouflage colour must not determine temperature.
            float textureDetail = dot(detail.rgb, float3(0.2126,0.7152,0.0722));
            float facing = abs(dot(normalize(i.normal), normalize(_WorldSpaceCameraPos-i.world)));
            heat *= 1 + (textureDetail-0.5)*_DetailAmount + (facing-0.5)*0.08;
            if (_Effect > 0.5) {
                float alpha = (_UseAlpha > 0.5 ? detail.a : 1.0) * i.color.a;
                clip(alpha - 0.05);
                heat *= pow(saturate(alpha), 1.2);
            }
            float transmission = exp(-i.eye * (0.000016 + _Atmosphere*0.00010));
            return lerp(0.09, max(0.02,heat), transmission);
        }
        float Background(v2f_img i):SV_Target {
            float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
            float distance = LinearEyeDepth(depth);
            float3 sceneColor = tex2D(_MainTex, i.uv).rgb;
            float lum = dot(sceneColor, float3(0.2126, 0.7152, 0.0722));
            // Background remains cool terrain; stable response prevents flickering
            float heat = 0.07 + 0.035 * saturate(log2(1 + max(0, min(lum, 0.5))));
            // Intense visual fire/burns (HDR luminescence > 0.8) contribute natural heat
            if (lum > 0.8) {
                heat += saturate((lum - 0.8) * 0.35);
            }
            if (distance > _ProjectionParams.z * 0.98) heat = 0.035;
            return lerp(0.09, heat, exp(-distance * (0.000016 + _Atmosphere * 0.00010)));
        }
        float3 Iron(float t) {
            float3 a=float3(0.015,0.008,0.025), b=float3(0.19,0.025,0.33);
            float3 c=float3(0.63,0.055,0.30), d=float3(0.96,0.32,0.025);
            float3 e=float3(1,0.78,0.17), f=float3(1,0.985,0.89);
            if(t<0.22) return lerp(a,b,t/0.22);
            if(t<0.46) return lerp(b,c,(t-0.22)/0.24);
            if(t<0.68) return lerp(c,d,(t-0.46)/0.22);
            if(t<0.88) return lerp(d,e,(t-0.68)/0.20);
            return lerp(e,f,(t-0.88)/0.12);
        }
        float4 Palette(v2f_img i):SV_Target {
            float heat=tex2D(_MainTex,i.uv).r;
            // Enhanced contrast mapping separating cold background (~0.07-0.10) from warm vehicles (0.35-0.70)
            float t=saturate(log2(1+max(0,heat-0.03)*3.5)/log2(1+max(0.2,_Span)*3.5));
            float noise=frac(sin(dot(floor(i.uv*_MainTex_TexelSize.zw),float2(12.9898,78.233))+floor(_Time.y*24))*43758.5453)-0.5;
            t=saturate(t+noise*_Noise);

            // White Hot: crisp contrast curve, hot vehicles glow brilliantly against dark background
            float whiteSignal = saturate((t - 0.08) / 0.85);
            float whiteHot = _WhiteHotCeiling * saturate(pow(whiteSignal, 0.90) * 1.08 + noise*_Noise);

            // Black Hot: clear tactical contrast where hot vehicles stand out as deep black silhouettes against clean light ground
            float blackHot = saturate(lerp(0.85, 0.02, pow(saturate((t - 0.05) / 0.88), 0.80)) - noise*_Noise);

            float3 rgb = _Mode<0.5 ? Iron(t) : (_Mode<1.5 ? whiteHot.xxx : blackHot.xxx);
            // URP target is linear; palettes above are defined in display space.
            return float4(GammaToLinearSpace(rgb),1);
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
