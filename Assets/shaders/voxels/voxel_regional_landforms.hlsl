#include "shaders/voxels/voxel_erosion.hlsl"
#include "shaders/voxels/voxel_mountain_masses.hlsl"
#include "shaders/voxels/voxel_rivers.hlsl"

#include "shaders/voxels/voxel_landform_noise.hlsl"

float LandformEligibilityDerivative( float value, float amount )
{
	return amount <= 0.0 || amount >= 1.0 ? 0.0 : LandformSmoothDerivative( (value - (1.2 - 1.4 * amount)) / 0.2 ) / 0.2;
}

float LandformEligibility( float value, float amount )
{
	if ( amount <= 0.0 )
	{
		return 0.0;
	}
	if ( amount >= 1.0 )
	{
		return 1.0;
	}
	return LandformSmooth( (value - (1.2 - 1.4 * amount)) / 0.2 );
}

#include "shaders/voxels/voxel_biomes.hlsl"

float4 SampleVoxelBaseLandform( float2 position, float4 terrain, float4 scales, float ruggedness, out float2 gradient, out float peakFraction )
{
	uint seed = (uint)(int)terrain.x;
	float2 dn;
	float2 dm;
	float2 dp;
	float2 dq;
	float2 dhs;
	float2 dhp;
	float n = LandformNoise( position, scales.x, seed ^ 0xB5297A4Du, dn );
	float m = LandformNoise( position, scales.y, seed ^ 0x68E31DA4u, dm );
	float land = LandformEligibility( n, terrain.y );
	float2 dl = dn * LandformEligibilityDerivative( n, terrain.y );
	float amount = terrain.z * (2 - terrain.z);
	float mountainPreference = LandformEligibility( m, amount );
	float2 dmp = dm * LandformEligibilityDerivative( m, amount );
	float mountains = mountainPreference * mountainPreference * mountainPreference * LandformSmooth( (land - 0.5) * 2 );
	float2 dmountains = dmp * (3 * mountainPreference * mountainPreference * LandformSmooth( (land - 0.5) * 2 )) +
		dl * (mountainPreference * mountainPreference * mountainPreference * LandformSmoothDerivative( (land - 0.5) * 2 ) * 2);
	float p = LandformNoise( position, scales.z * 2, seed ^ 0x1B56C4E9u, dp );
	float q = LandformNoise( position, scales.z, seed ^ 0x7F4A7C15u, dq );
	float hillShape = LandformNoise( position, scales.z * 0.75, seed ^ 0x94D049BBu, dhs );
	float hillPatch = LandformNoise( position, scales.z * 5.0, seed ^ 0xD1B54A35u, dhp );
	float hillPreference = (1 - LandformEligibility( p, terrain.w )) * LandformSmooth( (hillPatch - 0.25) * 2.5 );
	float2 dh = dp * (-LandformEligibilityDerivative( p, terrain.w ) * LandformSmooth( (hillPatch - 0.25) * 2.5 )) +
		dhp * ((1 - LandformEligibility( p, terrain.w )) * LandformSmoothDerivative( (hillPatch - 0.25) * 2.5 ) * 2.5);
	float plains = (1 - mountains) * (1 - hillPreference * hillPreference);
	float2 dplains = -dmountains * (1 - hillPreference * hillPreference) - dh * ((1 - mountains) * 2 * hillPreference);
	float hills = 1 - mountains - plains;
	float2 dhills = -dmountains - dplains;
	float hillRelief = 0.7 * hillShape + 0.3 * q;
	float mountainHeight = 0.10 + 0.28 * q * q;
	float2 dmh = dq * (0.56 * q);
	// Hills and transitional mountains receive a smaller amplitude. Relative
	// shape masks fade the upper/lower relief range independently of altitude.
	float hillExposure = LandformSmooth( (hillRelief - 0.10) / 0.30 ) * LandformSmooth( (1 - hillRelief) / 0.20 );
	float mountainExposure = 0.35 * LandformSmooth( (q - 0.10) / 0.30 ) * LandformSmooth( (1 - q) / 0.20 );
	peakFraction = 0;
	[branch]
	if ( mountains > 0.5 )
	{
		float shapeBlend = LandformSmooth( (mountains - 0.5) / (1 - 0.5) );
		float2 dsb = dmountains * (LandformSmoothDerivative( (mountains - 0.5) / (1 - 0.5) ) / (1 - 0.5));
		float2 massGradient;
		float erosionStrength;
		float mass = SampleVoxelMountainMass( position, scales.z * 1.30, seed, massGradient, peakFraction, erosionStrength );
		float broadMountainHeight = 0.10 + 0.83 * mass;
		float massExposure = LandformSmooth( (mass - 0.02) / 0.18 ) * LandformSmooth( (1 - mass) / 0.1 ) * erosionStrength;
		mountainExposure = (1 - shapeBlend) * mountainExposure + shapeBlend * massExposure;
		dmh = dmh * (1 - shapeBlend) + massGradient * (0.83 * shapeBlend) + dsb * (broadMountainHeight - mountainHeight);
		mountainHeight = (1 - shapeBlend) * mountainHeight + shapeBlend * broadMountainHeight;
	}
	float exposure = 0.25 * hills * hillExposure + mountains * mountainExposure;
	float plainHeight = 0.035 + 0.015 * q;
	float hillHeight = plainHeight + 0.28 * hillRelief;
	float landHeight = plains * plainHeight + hills * hillHeight + mountains * mountainHeight;
	float2 dlh = dplains * plainHeight + dq * (plains * 0.015) +
		dhills * hillHeight + (dq * 0.099 + dhs * 0.196) * hills +
		dmountains * mountainHeight + dmh * mountains;
	float uplandWeight = LandformSmooth( (mountainPreference - 0.1) / 0.9 ) * LandformSmooth( (land - 0.65) / 0.35 );
	[branch]
	if ( uplandWeight > 0 )
	{
		float2 de;
		float elevation = LandformNoise( position, scales.y * 1.35, seed ^ 0xA24BAED5u, de );
		float2 duw = dmp * (LandformSmoothDerivative( (mountainPreference - 0.1) / 0.9 ) / 0.9 * LandformSmooth( (land - 0.65) / 0.35 )) +
			dl * (LandformSmooth( (mountainPreference - 0.1) / 0.9 ) * LandformSmoothDerivative( (land - 0.65) / 0.35 ) / 0.35);
		float support = 0.12 + 0.56 * LandformSmooth( (elevation - 0.15) / 0.7 );
		dlh += duw * (support * (0.7 + 0.3 * p)) +
			de * (uplandWeight * 0.56 * LandformSmoothDerivative( (elevation - 0.15) / 0.7 ) / 0.7 * (0.7 + 0.3 * p)) +
			dp * (uplandWeight * support * 0.3);
		landHeight += uplandWeight * support * (0.7 + 0.3 * p);
	}
	float oceanHeight = -0.06 - 0.64 * (1 - n) * (1 - n);
	// Differentiate the complete pre-erosion height, including regional blends.
	gradient = (dn * ((1 - land) * 1.28 * (1 - n)) + dlh * land + dl * (landHeight - oceanHeight)) * scales.w;
	return float4( ((1 - land) * oceanHeight + land * landHeight) * scales.w,
		land, mountains, exposure );
}

// GPU mirror of RegionalLandforms.SampleWorld; recipe constants owned by TerrainErosion.
// Height, mountain weight and normalized local peak fraction.
float3 SampleVoxelNaturalLandform( float2 position, float4 terrain, float4 scales, float ruggedness, float seaLevel )
{
	float2 gradient;
	float peakFraction;
	float4 sample = SampleVoxelBaseLandform( position, terrain, scales, ruggedness, gradient, peakFraction );
	float amplitude = 0.035 * sample.w *
		LandformSmooth( (sample.y - 0.65) / 0.35 ) * LandformSmooth( (sample.x - seaLevel - 64.0) / 256.0 ) * scales.w;
	[branch]
	if ( amplitude <= 0.0 )
	{
		return float3( sample.x, sample.z, peakFraction );
	}
	return float3( sample.x + VoxelErosionOffset( position, gradient, scales.z * 0.20, amplitude, (uint)(int)terrain.x ),
		sample.z, peakFraction );
}

float3 SampleVoxelLandform( float2 position, float4 terrain, float4 scales, float ruggedness, float seaLevel )
{
	float3 natural = SampleVoxelNaturalLandform( position, terrain, scales, ruggedness, seaLevel );
	float riverHeight = SampleVoxelRiver( position, natural.x, seaLevel ).x;
	natural.x = RefineVoxelBiomeHeight( position, terrain, scales, natural.x, riverHeight, natural.y, seaLevel );
	return natural;
}

// Density and water consume height only; unused semantic outputs are eliminated.
float SampleVoxelLandformHeight( float2 position, float4 terrain, float4 scales, float ruggedness, float seaLevel )
{
	return SampleVoxelLandform( position, terrain, scales, ruggedness, seaLevel ).x;
}
