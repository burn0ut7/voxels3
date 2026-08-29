// Shared regular-cell classification for production meshing and diagnostics.
uint ClassifyVoxelRegularCell(
	int3 globalCell,
	float cellSize,
	int worldSeed,
	int generatorVersion )
{
	uint caseIndex = 0;
	for ( uint corner = 0; corner < 8; corner++ )
	{
		float density = SampleVoxelSdf(
			globalCell + VoxelCornerOffset( corner ),
			cellSize,
			worldSeed,
			generatorVersion );
		if ( density <= 0.0 )
		{
			caseIndex |= 1u << corner;
		}
	}

	return caseIndex;
}
