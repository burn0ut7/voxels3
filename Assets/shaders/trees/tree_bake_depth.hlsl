// Capture depth through the production vertex path, including leaf facing.
StaticCombo( S_TREE_BAKE_DEPTH, F_TREE_BAKE_DEPTH, Sys( ALL ) );
#if S_TREE_BAKE_DEPTH
	float3 g_vTreeBakeCenter < Attribute( "TreeBakeCenter" ); >;
	float3 g_vTreeBakeDirection < Attribute( "TreeBakeDirection" ); >;
	float g_flTreeBakeDiameter < Attribute( "TreeBakeDiameter" ); >;
#endif

void TreeCaptureDepth( inout Material material )
{
	#if S_TREE_BAKE_DEPTH
		float depth = 0.5 + dot( material.WorldPosition - g_vTreeBakeCenter,
			g_vTreeBakeDirection ) / max( g_flTreeBakeDiameter, 1.0 );
		material.Albedo = saturate( depth ).xxx;
		material.Emission = float3( 0.0, 0.0, 0.0 );
		material.Metalness = 0.0;
		material.Transmission = 0.0;
	#endif
}
