/*
Copyright (c) 2025 Facepunch Studios Ltd

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

// Facepunch sbox-public 804420939f467a3fb13c534a5c63a415dd55bd58.
// Project override: evaluate receiver derivatives before the coverage exit.
// See Docs/ValidationEvidence/ShadowRing/README.md for qualification and removal criteria.

#ifndef DIRECTIONAL_LIGHT_SHADOW_HLSL
#define DIRECTIONAL_LIGHT_SHADOW_HLSL

#include "common/Bindless.hlsl"
#include "Shadows/ShadowFiltering.hlsl"

// I don't know
;

#include "common/utils/MSAAUtils.hlsl"

// don't have more than this for fucks sake
#define MAX_CASCADE_COUNT 4

cbuffer DirectionalLightCB
{
    float4 g_DirectionalLightColor; // w fogstrength
    float4 g_DirectionalLightDirection; // w blank

    float4x4 g_DirectionalLightWorldToShadowViewMatrices[MAX_CASCADE_COUNT];
	uint4 g_DirectionalLightShadowMapTextureIndex;
	uint g_DirectionalLightCascadeCount;
    float g_DirectionalLightInverseShadowMapSize;
	// Bindless index of the screen-space (contact) shadow mask, 0 if none.
	uint g_DirectionalLightScreenSpaceShadowIndex;
	bool g_DirectionalLightEnabled;
    float4 g_DirectionalLightCascadeHardness;
    float4 g_DirectionalLightCascadeSpheres[MAX_CASCADE_COUNT]; // xyz = world center, w = radius squared
    float4 g_DirectionalLightShadowBias; // per-cascade depth bias, scaled by cascade size
};

int DirectionalLightDebug < Attribute( "DirectionalLightDebug" ); >;

static const float3 DebugColors[4] = {
    float3( 1.0f, 0.0f, 0.0f ),
    float3( 0.0f, 1.0f, 0.0f ),
    float3( 0.0f, 0.0f, 1.0f ),
    float3( 1.0f, 1.0f, 0.0f )
};

int FindCascade( float3 worldPosition, out float3 posLs )
{
	posLs = 0;
	[unroll]
	for ( int i = 0; i < MAX_CASCADE_COUNT; i++ )
	{
		if ( i >= g_DirectionalLightCascadeCount )
			break;

		float3 toCenter = worldPosition - g_DirectionalLightCascadeSpheres[i].xyz;
		if ( dot( toCenter, toCenter ) < g_DirectionalLightCascadeSpheres[i].w )
		{
			posLs = mul( g_DirectionalLightWorldToShadowViewMatrices[i], float4( worldPosition, 1 ) ).xyz;
			return i;
		}
	}
	return -1;
}

struct DirectionalLightShadow
{
	static float SampleCascade( int cascadeIndex, float3 worldPosition, float2 screenPos )
    {
		float4x4 worldToShadow = g_DirectionalLightWorldToShadowViewMatrices[cascadeIndex];



		float3 positionLs = mul( worldToShadow, float4( worldPosition, 1.0f ) ).xyz;

        ShadowPCFInput pcfInput;
        pcfInput.ShadowMap = Bindless::GetTexture2D( g_DirectionalLightShadowMapTextureIndex[cascadeIndex] );
        pcfInput.ShadowPos = positionLs;
        pcfInput.InvShadowMapRes = g_DirectionalLightInverseShadowMapSize;
        pcfInput.Bias = g_DirectionalLightShadowBias[cascadeIndex];
		pcfInput.Hardness = g_DirectionalLightCascadeHardness[cascadeIndex];
        pcfInput.ScreenPos = screenPos;

        return SampleShadowPCF( pcfInput );
    }

	// Screen-space (contact) shadows precomputed into a full-screen mask by the ScreenSpaceShadows
	// component. 1 = lit, 0 = shadowed. Returns 1 (no occlusion) when no mask is bound (index 0).
	// The mask is a non-MSAA full-res texture, so composite it with MSAAUtils::GetSampleIndex to pick
	// the depth-matching gather lane - this keeps the mask pixel-perfect under MSAA.
	static float SampleScreenSpaceShadow( float4 vPositionSs )
	{
		#if ( S_TRANSLUCENT == 1 || PROGRAM != VFX_PROGRAM_PS )
        	return 1.0f;
		#endif

		if ( g_DirectionalLightScreenSpaceShadowIndex == 0 )
			return 1.0f;

        Texture2D tMask = Bindless::GetTexture2D(g_DirectionalLightScreenSpaceShadowIndex);
        vPositionSs.xy -= g_vViewportOffset.xy;
		return MSAAUtils::SampleRed( tMask, vPositionSs );
	}

	static float3 GetOccludedPosition( float3 fragPos )
    {
        if ( g_DirectionalLightCascadeCount == 0 )
            return fragPos;

		float3 posLs;
		int cascade = FindCascade( fragPos, posLs );

		if ( cascade < 0 )
		{
			cascade = (int)g_DirectionalLightCascadeCount - 1;
			posLs = mul( g_DirectionalLightWorldToShadowViewMatrices[cascade], float4( fragPos, 1 ) ).xyz;
		}

		float s = Bindless::GetTexture2D( g_DirectionalLightShadowMapTextureIndex[cascade] ).SampleLevel( g_sPointClamp, posLs.xy, 0 ).r;

		// zGrad is the gradient of shadow-Z w.r.t. world position; 1/|zGrad| converts shadow-Z delta to world units
		// Reversed-Z: s > posLs.z when occluder is closer to light than fragment
		float3 zGrad = g_DirectionalLightWorldToShadowViewMatrices[cascade][2].xyz;
        return fragPos + zGrad * max( s - posLs.z, 0.0f ) / dot( zGrad, zGrad );
    }

    static float GetVisibility( float3 worldPosition, float4 vPositionSs )
    {
        float ssShadow = SampleScreenSpaceShadow( vPositionSs );

        if ( g_DirectionalLightCascadeCount == 0 )
            return ssShadow;

		// Evaluate position derivatives before neighboring pixels leave shadow coverage.
		#if ( PROGRAM == VFX_PROGRAM_PS )
			float3 positionDx = ddx( worldPosition );
			float3 positionDy = ddy( worldPosition );
		#endif

		float3 posLs;
		int cascade = FindCascade( worldPosition, posLs );

		if ( cascade < 0 )
			return ssShadow;

		#if ( PROGRAM == VFX_PROGRAM_PS )
			// Same offset/kernel radius as the pinned ShadowFiltering.hlsl helper,
			// using the derivatives above so the remaining work stays inside coverage.
			float4x4 worldToShadow = g_DirectionalLightWorldToShadowViewMatrices[cascade];
			float3 normal = normalize( cross( positionDy, positionDx ) );
			float radiusTexels = 1.5 * min( UserShadowFilterQuality, 3 ) * rcp( max( g_DirectionalLightCascadeHardness[cascade], 1.0 ) ) + 1.0;
			worldPosition += normal * ( g_DirectionalLightInverseShadowMapSize / length( worldToShadow[0].xyz ) * radiusTexels );
		#endif

		return SampleCascade( cascade, worldPosition, vPositionSs.xy ) * ssShadow;
    }



    static float3 GetDebugColor( float3 worldPosition )
    {
		for ( int i = 0; i < (int)g_DirectionalLightCascadeCount; i++ )
		{
			float3 toCenter = worldPosition - g_DirectionalLightCascadeSpheres[i].xyz;
			if ( dot( toCenter, toCenter ) < g_DirectionalLightCascadeSpheres[i].w )
				return DebugColors[i];
		}

        return float3( 0.0f, 0.0f, 0.0f );
    }
};

#endif
