Shader "Inside Sphere Blend"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
		_SideTex("SideTexture", 2D) = "black" {}
		_ChromaTex("Chroma", 2D) = "white" {}
		_Blend("Blend", Range(0, 1)) = 1

		[KeywordEnum(None, Top_Bottom, Left_Right, Custom_UV)] Stereo ("Stereo Mode", Float) = 0
		[KeywordEnum(None, Left, Right)] ForceEye ("Force Eye Mode", Float) = 0
		[Toggle(STEREO_DEBUG)] _StereoDebug ("Stereo Debug Tinting", Float) = 0
		[KeywordEnum(None, EquiRect180)] Layout("Layout", Float) = 0
		[Toggle(HIGH_QUALITY)] _HighQuality ("High Quality", Float) = 0
		[Toggle(APPLY_GAMMA)] _ApplyGamma("Apply Gamma", Float) = 0
		[Toggle(USE_YPCBCR)] _UseYpCbCr("Use YpCbCr", Float) = 0
    }
    SubShader
    {
		Tags { "RenderType"="Opaque" "IgnoreProjector" = "True" "Queue" = "Background" }
		ZWrite On
		//ZTest Always
		Cull Front
		Lighting Off

        Pass
        {
            CGPROGRAM
			#include "UnityCG.cginc"
			#include "Assets/AVProVideo/Resources/Shaders/AVProVideo.cginc"
#if HIGH_QUALITY || APPLY_GAMMA
			#pragma target 3.0
#endif
            #pragma vertex vert
            #pragma fragment frag

			//#define STEREO_DEBUG 1
			//#define HIGH_QUALITY 1

			#pragma multi_compile_fog
			#pragma multi_compile MONOSCOPIC STEREO_TOP_BOTTOM STEREO_LEFT_RIGHT STEREO_CUSTOM_UV

			// TODO: Change XX_OFF to __ for Unity 5.0 and above
			// this was just added for Unity 4.x compatibility as __ causes
			// Android and iOS builds to fail the shader
			#pragma multi_compile STEREO_DEBUG_OFF STEREO_DEBUG
			#pragma multi_compile FORCEEYE_NONE FORCEEYE_LEFT FORCEEYE_RIGHT
			#pragma multi_compile HIGH_QUALITY_OFF HIGH_QUALITY
			#pragma multi_compile APPLY_GAMMA_OFF APPLY_GAMMA
			#pragma multi_compile USE_YPCBCR_OFF USE_YPCBCR
			#pragma multi_compile LAYOUT_NONE LAYOUT_EQUIRECT180

            struct appdata
            {
                float4 vertex : POSITION; // vertex position
#if HIGH_QUALITY
				float3 normal : NORMAL;
#else
                float2 uv : TEXCOORD0; // texture coordinate			
#if STEREO_CUSTOM_UV
				float2 uv2 : TEXCOORD1;	// Custom uv set for right eye (left eye is in TEXCOORD0)
#endif
#endif
				
            };

            struct v2f
            {
                float4 vertex : SV_POSITION; // clip space position
#if HIGH_QUALITY
				float3 normal : TEXCOORD0;
				
#if STEREO_TOP_BOTTOM | STEREO_LEFT_RIGHT
				float4 scaleOffset : TEXCOORD1; // texture coordinate
#if UNITY_VERSION >= 500
				UNITY_FOG_COORDS(2)
#endif
#else
#if UNITY_VERSION >= 500
				UNITY_FOG_COORDS(1)
#endif
#endif
#else
                float2 uv : TEXCOORD0; // texture coordinate
				//float2 uvOther: TEXCOORD0  // UV coordinate of side texture.

#if UNITY_VERSION >= 500
				UNITY_FOG_COORDS(1)
#endif
#endif

#if STEREO_DEBUG
				float4 tint : COLOR;
#endif
            };

            uniform sampler2D _MainTex;
            uniform sampler2D _SideTex;
#if USE_YPCBCR
			uniform sampler2D _ChromaTex;
#endif
			uniform float4 _MainTex_ST;
			uniform float3 _cameraPosition;
			uniform float _Blend;

            v2f vert (appdata v)
            {
                v2f o;
                
				o.vertex = XFormObjectToClip(v.vertex);

#if !HIGH_QUALITY
				o.uv.xy = TRANSFORM_TEX(v.uv, _MainTex);
				#if LAYOUT_EQUIRECT180
				o.uv.x = ((o.uv.x - 0.5) * 2.0) + 0.5;
				#endif				
                o.uv.xy = float2(1.0-o.uv.x, o.uv.y);
#endif

#if STEREO_TOP_BOTTOM | STEREO_LEFT_RIGHT
				float4 scaleOffset = GetStereoScaleOffset(IsStereoEyeLeft(_cameraPosition, UNITY_MATRIX_V[0].xyz), _MainTex_ST.y < 0.0);

				#if !HIGH_QUALITY
				o.uv.xy *= scaleOffset.xy;
				o.uv.xy += scaleOffset.zw;
				#else
				o.scaleOffset = scaleOffset;
				#endif
#elif STEREO_CUSTOM_UV && !HIGH_QUALITY
				if (!IsStereoEyeLeft(_cameraPosition, UNITY_MATRIX_V[0].xyz))
				{
					o.uv.xy = TRANSFORM_TEX(v.uv2, _MainTex);
					o.uv.xy = float2(1.0 - o.uv.x, o.uv.y);
				}
#endif

#if HIGH_QUALITY
				o.normal = v.normal;
#endif

				#if STEREO_DEBUG
				o.tint = GetStereoDebugTint(IsStereoEyeLeft(_cameraPosition, UNITY_MATRIX_V[0].xyz));
				#endif

#if UNITY_VERSION >= 500
				UNITY_TRANSFER_FOG(o, o.vertex);
#endif

                return o;
			}

            
            fixed4 frag (v2f i) : SV_Target
            {
				float2 uv;
				fixed4 col1;
				fixed4 col2;

#if HIGH_QUALITY
				float3 n = normalize(i.normal);

				float M_1_PI = 1.0 / 3.1415926535897932384626433832795;
				float M_1_2PI = 1.0 / 6.283185307179586476925286766559;
				uv.x = 0.5 - atan2(n.z, n.x) * M_1_2PI;
				uv.y = 0.5 - asin(-n.y) * M_1_PI;
				uv.x += 0.75;
				uv.x = fmod(uv.x, 1.0);
				//uv.x = uv.x % 1.0;
				uv.xy = TRANSFORM_TEX(uv, _MainTex);
				#if LAYOUT_EQUIRECT180
				uv.x = ((uv.x - 0.5) * 2.0) + 0.5;
				#endif
				#if STEREO_TOP_BOTTOM | STEREO_LEFT_RIGHT
				uv.xy *= i.scaleOffset.xy;
				uv.xy += i.scaleOffset.zw;
				#endif

#else
				uv = i.uv;
#endif
#if USE_YPCBCR
	#if SHADER_API_METAL || SHADER_API_GLES || SHADER_API_GLES3
				float3 ypcbcr = float3(tex2D(_MainTex, uv).r, tex2D(_ChromaTex, uv).rg);
				float3 ypcbcr2 = float3(tex2D(_SideTex, uv).r, tex2D(_ChromaTex, uv).rg);
	#else
				float3 ypcbcr = float3(tex2D(_MainTex, uv).r, tex2D(_ChromaTex, uv).ra);
				float3 ypcbcr2 = float3(tex2D(_SideTex, uv).r, tex2D(_ChromaTex, uv).ra);
	#endif
				col1 = fixed4(Convert420YpCbCr8ToRGB(ypcbcr), 1.0);
				col2 = fixed4(Convert420YpCbCr8ToRGB(ypcbcr2), 1.0);
#else
                col1 = tex2D(_MainTex, uv);
                col2 = tex2D(_SideTex, uv);
#endif
				fixed4 col = col1 * _Blend + col2 * (1.0 - _Blend);
#if APPLY_GAMMA
				col.rgb = GammaToLinear(col.rgb);
#endif

#if STEREO_DEBUG
				col *= i.tint;
#endif

#if UNITY_VERSION >= 500
				UNITY_APPLY_FOG(i.fogCoord, col);
#endif
#if defined(SHADERLAB_GLSL)
				col.rgb = float3(1, 0, 0);
#endif

                return fixed4(col.rgb, 1.0);
            }
            ENDCG
        }
    }
}
