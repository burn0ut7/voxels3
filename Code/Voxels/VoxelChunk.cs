using System;

/// <summary>
/// Authoritative SDF samples for one spatial chunk. Density below zero is solid,
/// density above zero is air, and zero is the terrain surface.
/// </summary>
public sealed class VoxelChunk
{
	private readonly float[] _densitySamples;

	public Vector3Int Coordinate { get; }
	public int CellsPerAxis { get; }
	public int SamplesPerAxis { get; }
	public float CellSize { get; }
	public int SampleCount => _densitySamples.Length;
	public long DensityBytes => (long)_densitySamples.Length * sizeof( float );
	public float MinimumDensity { get; }
	public float MaximumDensity { get; }
	public string HumanName => $"Chunk X {Coordinate.x}, Y {Coordinate.y}, Z {Coordinate.z}";
	public string LogId => $"C[{Coordinate.x},{Coordinate.y},{Coordinate.z}]";

	public VoxelChunk( Vector3Int coordinate, int cellsPerAxis, float cellSize, float terrainSurfaceHeight )
	{
		Coordinate = coordinate;
		CellsPerAxis = cellsPerAxis;
		SamplesPerAxis = cellsPerAxis + 1;
		CellSize = cellSize;

		var sampleCount = checked( SamplesPerAxis * SamplesPerAxis * SamplesPerAxis );
		_densitySamples = new float[sampleCount];

		var chunkWorldSize = cellsPerAxis * cellSize;
		var chunkMinimumZ = coordinate.z * chunkWorldSize;
		var samplesPerLayer = SamplesPerAxis * SamplesPerAxis;
		var minimumDensity = float.MaxValue;
		var maximumDensity = float.MinValue;

		for ( var z = 0; z < SamplesPerAxis; z++ )
		{
			var density = chunkMinimumZ + z * cellSize - terrainSurfaceHeight;
			Array.Fill( _densitySamples, density, z * samplesPerLayer, samplesPerLayer );
			minimumDensity = Math.Min( minimumDensity, density );
			maximumDensity = Math.Max( maximumDensity, density );
		}

		MinimumDensity = minimumDensity;
		MaximumDensity = maximumDensity;
	}

	public bool TryGetDensity( Vector3Int localSample, out float density )
	{
		if ( localSample.x < 0 || localSample.x >= SamplesPerAxis ||
			localSample.y < 0 || localSample.y >= SamplesPerAxis ||
			localSample.z < 0 || localSample.z >= SamplesPerAxis )
		{
			density = 0f;
			return false;
		}

		var index = localSample.x + SamplesPerAxis *
			(localSample.y + SamplesPerAxis * localSample.z);
		density = _densitySamples[index];
		return true;
	}
}
