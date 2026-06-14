// EchoSpace AnchorRimSimple — 簡化版 PreviewMarker / AnchorPin 外層 rim
//
// 邏輯：
// - 依賴 ZTest LEqual 與 render queue（點雲先畫、rim 後畫）做深度遮擋
// - ZWrite Off：anchor 不寫入 depth buffer，避免污染後續遮擋
// - 有場景 / 點雲擋在前面 → rim 被遮；沒有遮擋 → rim 顯示
//
// Unity 使用：
//   DepthRim / DepthShell  → EchoSpace/AnchorRimSimple (scale 0.09～0.12)
//   BrightCore             → EchoSpace/OverlayAlwaysOnTop (scale 0.05～0.06)
//
Shader "EchoSpace/AnchorRimSimple"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.85, 0.2, 0.8)
        _RimWidth ("Rim Width", Range(0, 1)) = 0.45
        _RimPower ("Rim Power", Range(1, 8)) = 2.5
    }

    SubShader
    {
        Tags { "Queue" = "Geometry+1" "RenderType" = "Transparent" }

        Pass
        {
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _RimWidth;
            float _RimPower;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);

                float3 viewPos = mul(UNITY_MATRIX_MV, v.vertex).xyz;
                o.normal = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));
                o.viewDir = normalize(-viewPos);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float NdotV = saturate(dot(normalize(i.normal), normalize(i.viewDir)));
                float rim = pow(1.0 - NdotV, _RimPower);
                float outerRim = smoothstep(1.0 - _RimWidth, 1.0, rim);

                if (outerRim < 0.01)
                    discard;

                return fixed4(_Color.rgb, _Color.a * outerRim);
            }
            ENDCG
        }
    }

    FallBack Off
}
