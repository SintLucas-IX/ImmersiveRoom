// Voegt het beeld van het linker- en rechteroog samen tot één anaglyph (3D met een rood/blauw- of rood/cyaan-brilletje).
// _MainTex = linkeroog (rood glas), _RechtsTex = rechteroog (blauw of cyaan glas).
Shader "Systeem/Anaglyph"
{
    Properties
    {
        _MainTex ("Linkeroog", 2D) = "black" {}
        _RechtsTex ("Rechteroog", 2D) = "black" {}
        _Modus ("Bril: 0 rood/blauw, 1 rood/cyaan kleur, 2 rood/cyaan Dubois, 3 rood/cyaan grijs, 4 gekalibreerd", Float) = 3
        _KleurLinks ("Gekalibreerd: kleur voor het linkeroog (rood glas)", Color) = (0.706, 0, 0, 1)
        _KleurRechts ("Gekalibreerd: kleur voor het rechteroog (blauw glas)", Color) = (0, 0, 0.392, 1)
        _Helderheid ("Helderheid", Float) = 1.6
        _Gamma ("Gamma (kleiner dan 1 = donkere delen lichter)", Float) = 0.8
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _RechtsTex;
            float _Modus;
            float _Helderheid;
            float _Gamma;
            float4 _KleurLinks;
            float4 _KleurRechts;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 L = tex2D(_MainTex, i.uv).rgb;
                float3 R = tex2D(_RechtsTex, i.uv).rgb;
                float3 grijs = float3(0.299, 0.587, 0.114);
                float3 kleur;

                if (_Modus > 3.5)
                {
                    // Gekalibreerd op de brillen in de Immersive Room (kleurtest F8): elk oog krijgt de kleur die het andere glas
                    // het best tegenhoudt. De helderheid van elk oog komt uit het beeld (grijswaarde).
                    float gl = pow(saturate(dot(L, grijs) * _Helderheid), _Gamma);
                    float gr = pow(saturate(dot(R, grijs) * _Helderheid), _Gamma);
                    return fixed4(gl * _KleurLinks.rgb + gr * _KleurRechts.rgb, 1);
                }

                if (_Modus < 0.5)
                {
                    // Rood/blauw: links in rood, rechts in blauw. We nemen het felste kanaal per pixel (niet de
                    // luminantie): anders worden blauwe objecten bijna zwart, want blauw telt maar 11% mee.
                    float vl = max(L.r, max(L.g, L.b));
                    float vr = max(R.r, max(R.g, R.b));
                    kleur = float3(lerp(dot(L, grijs), vl, 0.75), 0, lerp(dot(R, grijs), vr, 0.75));
                }
                else if (_Modus < 1.5)
                {
                    // Rood/cyaan (kleur): rood van links, groen en blauw van rechts
                    kleur = float3(L.r, R.g, R.b);
                }
                else if (_Modus > 2.5)
                {
                    // Rood/cyaan in grijswaarden: rustigst, geen kleurconflict (zoals het schaakbord-voorbeeld)
                    float gl = dot(L, grijs);
                    float gr = dot(R, grijs);
                    kleur = float3(gl, gr, gr);
                }
                else
                {
                    // Rood/cyaan volgens Dubois (minder spookbeelden)
                    float3x3 ml = float3x3( 0.437,  0.449,  0.164,
                                           -0.062, -0.062, -0.024,
                                           -0.048, -0.050, -0.017);
                    float3x3 mr = float3x3(-0.011, -0.032, -0.007,
                                            0.377,  0.761,  0.009,
                                           -0.026, -0.093,  1.234);
                    kleur = saturate(mul(ml, L) + mul(mr, R));
                }
                // Door een gekleurd glas zie je maar één kleur: daarom lichter maken
                kleur = pow(saturate(kleur * _Helderheid), _Gamma);
                return fixed4(kleur, 1);
            }
            ENDCG
        }
    }
}
