#ifndef TREE_LOD_FADE_HLSL
#define TREE_LOD_FADE_HLSL

float g_flTreeLodFade < Attribute( "TreeLodFade" ); Default( 1.0 ); >;

float g_flTreeLodFar < Default( 0.0 ); >;

float TreePixelDither( float2 pixel )
{
	return frac( 52.9829189 * frac( dot( floor( pixel ), float2( 0.06711056, 0.00583715 ) ) ) );
}

void TreeDetailedFade( float2 pixel )
{
	float coverage = g_flTreeLodFade - TreePixelDither( pixel );
	clip( g_flTreeLodFar > 0.5 ? -coverage : coverage );
}

#endif
