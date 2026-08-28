// Canonical GPU SDF sampling boundary. Sample coordinates are global integer
// lattice coordinates so adjacent chunks evaluate shared corners identically.
float SampleVoxelSdf( int3 globalSampleCoordinate, float cellSize, float surfaceHeight )
{
	return (float)globalSampleCoordinate.z * cellSize - surfaceHeight;
}

float SampleVoxelSdfWorld( float3 worldPosition, float surfaceHeight )
{
	return worldPosition.z - surfaceHeight;
}

int3 VoxelCornerOffset( uint cornerIndex )
{
	return int3( cornerIndex & 1, (cornerIndex >> 1) & 1, (cornerIndex >> 2) & 1 );
}

float3 SampleVoxelSdfGradient( float3 worldPosition, float cellSize, float surfaceHeight )
{
	float stepSize = cellSize * 0.5;
	return float3(
		SampleVoxelSdfWorld( worldPosition + float3( stepSize, 0, 0 ), surfaceHeight ) -
			SampleVoxelSdfWorld( worldPosition - float3( stepSize, 0, 0 ), surfaceHeight ),
		SampleVoxelSdfWorld( worldPosition + float3( 0, stepSize, 0 ), surfaceHeight ) -
			SampleVoxelSdfWorld( worldPosition - float3( 0, stepSize, 0 ), surfaceHeight ),
		SampleVoxelSdfWorld( worldPosition + float3( 0, 0, stepSize ), surfaceHeight ) -
			SampleVoxelSdfWorld( worldPosition - float3( 0, 0, stepSize ), surfaceHeight ) );
}
