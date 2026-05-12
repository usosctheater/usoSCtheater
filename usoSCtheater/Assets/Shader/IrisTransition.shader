Shader "Custom/IrisTransition"
{
    Properties
    {
        _MainTex   ("Mask Texture", 2D) = "white" {}
        _Cutoff    ("Cutoff", Range(0,1)) = 1.0
        _Color     ("Color", Color)     = (0,0,0,1)
        _Center    ("Center", Vector)   = (0.5, 0.5, 0, 0)  // 화면 중심
        _Scale     ("Scale", Float)     = 1.0               // 아이콘 크기
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            sampler2D _MainTex;
            float     _Cutoff;
            float4    _Color;
            float4    _Center;
            float     _Scale;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 화면 중심 기준으로 UV 재계산
                float2 centered = (i.uv - _Center.xy) / _Scale + 0.5;

                // 화면 범위를 벗어난 픽셀은 검정으로 처리
                if (centered.x < 0 || centered.x > 1 ||
                    centered.y < 0 || centered.y > 1)
                {
                    return fixed4(_Color.rgb, 1.0);
                }

                fixed4 mask = tex2D(_MainTex, centered);
                float brightness = (mask.r + mask.g + mask.b) / 3.0;

                // Cutoff보다 밝은 픽셀 = 구멍 (투명)
                // Cutoff보다 어두운 픽셀 = 덮음 (불투명)
                float alpha = step(brightness, _Cutoff);

                return fixed4(_Color.rgb, alpha);
            }
            ENDCG
        }
    }
}