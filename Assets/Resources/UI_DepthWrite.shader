Shader "Custom/UI_FullDepth_AlphaClip"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color   ("Tint", Color) = (1,1,1,1)
        _Cutoff  ("Alpha Cutoff", Range(0,1)) = 0.02
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha

        // PASS 1: tulis depth berdasarkan alpha
        ZWrite On
        ZTest LEqual
        ColorMask 0
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Color;
            float  _Cutoff;

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert(appdata v){
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 fragDepth(v2f i):SV_Target{
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                clip(c.a - _Cutoff); // hanya tulis depth bila alpha > cutoff
                return 0;
            }
            ENDCG
        }

        // PASS 2: gambar warna UI (hormati depth)
        ZWrite Off
        ZTest LEqual
        ColorMask RGBA
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragColor
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Color;

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert(appdata v){
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 fragColor(v2f i):SV_Target{
                return tex2D(_MainTex, i.uv) * _Color;
            }
            ENDCG
        }
    }
}
