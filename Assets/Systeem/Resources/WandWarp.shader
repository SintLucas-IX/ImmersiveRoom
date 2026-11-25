// Zet het beeld van een wand vervormd op de beamer, zodat het precies op de echte wand valt (zie WandKalibratie).
// _H is een homografie: van een punt op de beamer (0-1) naar een punt in het wandbeeld (0-1). Buiten de wand: zwart.
Shader "Systeem/WandWarp"
{
    Properties
    {
        [PerRendererData] _MainTex ("Wandbeeld", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4x4 _H;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 p = mul((float3x3)_H, float3(i.uv, 1));
                if (p.z <= 0) return fixed4(0, 0, 0, 1);
                float2 uv = p.xy / p.z;
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return fixed4(0, 0, 0, 1);
                fixed4 c = tex2Dlod(_MainTex, float4(uv, 0, 0));
                return fixed4(c.rgb * i.color.rgb, 1);
            }
            ENDCG
        }
    }
}
