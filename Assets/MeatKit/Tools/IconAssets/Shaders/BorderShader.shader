// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Image/BorderEffect"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_Thickness("Thickness", Range(0, 50)) = 5
		_BorderColor("Border Color", Color) = (1, 0, 0, 1)
	}
	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma exclude_renderers d3d11_9x
			#pragma exclude_renderers d3d9
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};
			

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				return o;
			}
			
			
			sampler2D _MainTex;
			float4 _MainTex_ST, _MainTex_TexelSize;
			float _Thickness;
			fixed4 _BorderColor;
			
			fixed4 frag (v2f i) : SV_Target
			{
				fixed4 col = tex2D(_MainTex, i.uv);

				float iterations = min(50, _Thickness);

				for (int k = 0; k < iterations; k += 1)
				{
					for (int j = 0; j < iterations; j += 1)
					{
						float2 PixelPos = i.uv.xy + float2((k - iterations / 2) * _MainTex_TexelSize.x, (j - iterations / 2) * _MainTex_TexelSize.y);
						float4 Pixel = tex2D(_MainTex, PixelPos);

						if (Pixel.a > 0 && col.a == 0) return _BorderColor;

					}
				}

				return col;
			}
			ENDCG
		}
	}
}
