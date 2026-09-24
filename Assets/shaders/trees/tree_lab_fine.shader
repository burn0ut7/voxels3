HEADER
{
	Description = "Attached fine branches from Blender";
}
FEATURES
{
	#include "common/features.hlsl"
	Feature( F_TREE_MOTION, 0..1, "Tree Motion" );
	Feature( F_TREE_BAKE_DEPTH, 0..1, "Authoring Depth Capture" );
}
MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}
COMMON
{
	#include "common/shared.hlsl"
}
struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
};
VS
{
	#include "common/vertex.hlsl"
	#include "trees/tree_wood_motion.hlsl"
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		TreeWoodMotion( i, o );
		return FinalizeVertex( o );
	}
}
PS
{
	BoolAttribute( VertexNeedsPropOrigin, true );
	#include "common/utils/Material.CommonInputs.hlsl"
	#include "common/pixel.hlsl"
	#include "trees/tree_lod_fade.hlsl"
	#include "trees/tree_bake_depth.hlsl"
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		TreeDetailedFade( i.vPositionSs.xy );
		Material m = Material::From( i );
		m.Metalness = 0.0;
		m.Transmission = 0.0;
		TreeCaptureDepth( m );
		return ShadingModelStandard::Shade( i, m );
	}
}
