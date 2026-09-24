#ifndef TREE_CUT_HLSL
#define TREE_CUT_HLSL

float g_flTreeCutHeight < Attribute( "TreeCutHeight" ); Default( -100000.0 ); >;

void TreeCut( float localHeight )
{
	clip( localHeight - g_flTreeCutHeight );
}

#endif
