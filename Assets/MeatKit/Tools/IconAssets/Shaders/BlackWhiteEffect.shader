Shader "Image/BlackWhiteEffect"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_MinBrightness("Min Brightness", Float) = 0
		_MaxBrightness("Max Brightness", Float) = 1
		_ColorBands("Color Bands", Float) = 1000
		_DarkColor("Dark Color", Color) = (0, 0, 0, 1)
		_BrightColor("Bright Color", Color) = (1, 1, 1, 1)
		_LightToNormalFactor("Light To Normal Factor", Range(0, 1)) = 1
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
			
			float remap(float value, float from1, float to1, float from2, float to2) {
				return (value - from1) / (to1 - from1) * (to2 - from2) + from2;
			}


			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				return o;
			}
			
			sampler2D _MainTex, _CameraDepthNormalsTexture;
			float _MinBrightness, _MaxBrightness, _ColorBands, _LightToNormalFactor;
			fixed4 _DarkColor, _BrightColor;
			
			fixed4 frag (v2f i) : SV_Target
			{
				float4 depthnormal = tex2D(_CameraDepthNormalsTexture, i.uv);
				fixed4 col = tex2D(_MainTex, i.uv);

				//decode depthnormal
				float3 normal;
				float depth;
				DecodeDepthNormal(depthnormal, depth, normal);

				//Get the luminance and process it
				fixed normalLuminance = Luminance(fixed3(normal.b, normal.b, normal.b));
				fixed lightLuminance = Luminance(fixed3(col.r, col.g, col.b));

				fixed luminance = lerp(normalLuminance, lightLuminance, _LightToNormalFactor);

				luminance = remap(luminance, 0, 1, _MinBrightness, _MaxBrightness);
				luminance = floor(luminance * _ColorBands) / _ColorBands;

				float depthFactor = 1 - step(1, depth);
				fixed4 output = lerp(_DarkColor, _BrightColor, luminance);
				output = fixed4(output.r, output.g, output.b, depthFactor);

				return output;
			}
			ENDCG
		}
	}
}
