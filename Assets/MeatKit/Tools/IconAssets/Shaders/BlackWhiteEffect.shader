Shader "Image/BlackWhiteEffect"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_ClipDistance("Clip Distance", Float) = 10
		_NormalPower("Normal Power", Float) = 2
		_NormalMultiplier("Normal Multiplier", Float) = 100
		_NormalCutoff("Normal Cutoff", Range(0, 1)) = 0.5
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
			
			float when_lt(float x, float y) {
				return max(sign(y - x), 0.0);
			}

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				return o;
			}
			
			sampler2D _MainTex, _CameraDepthNormalsTexture;
			float _ClipDistance, _NormalPower, _NormalMultiplier, _NormalCutoff;
			
			fixed4 frag (v2f i) : SV_Target
			{
				fixed4 col = tex2D(_MainTex, i.uv);
				
				float4 depthnormal = tex2D(_CameraDepthNormalsTexture, i.uv);

				//decode depthnormal
				float3 normal;
				float depth;
				DecodeDepthNormal(depthnormal, depth, normal);


				fixed luminance = Luminance(fixed3(normal.b, normal.b, normal.b));

				luminance = pow(luminance, _NormalPower) * _NormalMultiplier;

				luminance = when_lt(_NormalCutoff, luminance);

				float depthFactor = when_lt(depth, 1);
				//float depthFactor = 1;
				col = fixed4(luminance, luminance, luminance, depthFactor);

				return col;
			}
			ENDCG
		}
	}
}
