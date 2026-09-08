internal static class TerrainClipboxLimits
{
	public const int MaximumSupportedVisualLod = 6;
	public const int SupportedVisualLevelCount = MaximumSupportedVisualLod + 1;
}

internal readonly record struct GpuMeshRegionKey( int Level, Vector3Int Coordinate );

internal enum GpuTransitionFace
{
	NegativeX,
	PositiveX,
	NegativeY,
	PositiveY,
	NegativeZ,
	PositiveZ
}

internal readonly record struct GpuTransitionKey(
	int FineLevel,
	int CoarseLevel,
	Vector3Int CoarseCoordinate,
	GpuTransitionFace Face );

internal readonly record struct GpuSdfDescriptor(
	GpuMeshRegionKey Key,
	int CellsPerAxis,
	float CellSize,
	ProceduralTerrainSettings TerrainSettings,
	int GeneratorVersion,
	int SourceRevision )
{
	public Vector3Int ChunkCoordinate => Key.Coordinate;
	public TerrainFieldSnapshot Field { get; init; }
	public int EditRevision { get; init; }
	public int FieldEpoch { get; init; }
	public SdfWorldAabb SamplingBounds
	{
		get
		{
			var size = CellsPerAxis * CellSize;
			var origin = new Vector3( Key.Coordinate.x * size, Key.Coordinate.y * size, Key.Coordinate.z * size );
			return new SdfWorldAabb( origin - new Vector3( CellSize ), origin + new Vector3( size + CellSize ) );
		}
	}

	public bool MatchesField( TerrainFieldSnapshot field ) => FieldEpoch == field.Epoch &&
		EditRevision == field.GetCorrectionRange( SamplingBounds, out _, out _ );

	public GpuSdfDescriptor WithField( TerrainFieldSnapshot field, bool captureRegion = true )
	{
		var revision = field.GetCorrectionRange( SamplingBounds, out _, out _ );
		if ( EditRevision == revision && FieldEpoch == field.Epoch && (revision == 0 || Field is not null) ) return this;
		return this with { Field = !captureRegion || revision == 0 ? null : field.CaptureRegion( SamplingBounds, pinSamples: false ), EditRevision = revision, FieldEpoch = field.Epoch };
	}

	// Full immutable snapshots may differ because an unrelated page changed.
	// Only this region's dependency revision participates in cache identity.
	public bool Equals( GpuSdfDescriptor other ) => Key == other.Key &&
		CellsPerAxis == other.CellsPerAxis && CellSize == other.CellSize &&
		TerrainSettings == other.TerrainSettings && GeneratorVersion == other.GeneratorVersion &&
		SourceRevision == other.SourceRevision && EditRevision == other.EditRevision && FieldEpoch == other.FieldEpoch;

	public override int GetHashCode() => System.HashCode.Combine( Key, CellsPerAxis, CellSize,
		TerrainSettings, GeneratorVersion, SourceRevision, EditRevision, FieldEpoch );

	public static GpuSdfDescriptor FromChunk(
		VoxelChunk chunk,
		int sourceRevision )
	{
		return new GpuSdfDescriptor(
			new GpuMeshRegionKey( 0, chunk.Coordinate ),
			chunk.CellsPerAxis,
			chunk.CellSize,
			chunk.TerrainSettings,
			ProceduralTerrainSdf.CurrentVersion,
			sourceRevision ).WithField( chunk.Field );
	}
}

internal readonly record struct GpuTransitionDescriptor(
	GpuTransitionKey Key,
	int CellsPerAxis,
	float FineCellSize,
	float CoarseCellSize,
	ProceduralTerrainSettings TerrainSettings,
	int GeneratorVersion,
	int SourceRevision )
{
	public TerrainFieldSnapshot Field { get; init; }
	public int EditRevision { get; init; }
	public int FieldEpoch { get; init; }
	public SdfWorldAabb SamplingBounds
	{
		get
		{
			var size = CellsPerAxis * CoarseCellSize;
			var minimum = new Vector3( Key.CoarseCoordinate.x * size, Key.CoarseCoordinate.y * size, Key.CoarseCoordinate.z * size );
			var maximum = minimum + new Vector3( size );
			switch ( Key.Face )
			{
				case GpuTransitionFace.NegativeX: maximum.x = minimum.x; break;
				case GpuTransitionFace.PositiveX: minimum.x = maximum.x; break;
				case GpuTransitionFace.NegativeY: maximum.y = minimum.y; break;
				case GpuTransitionFace.PositiveY: minimum.y = maximum.y; break;
				case GpuTransitionFace.NegativeZ: maximum.z = minimum.z; break;
				case GpuTransitionFace.PositiveZ: minimum.z = maximum.z; break;
			}
			return new SdfWorldAabb( minimum - new Vector3( CoarseCellSize ), maximum + new Vector3( CoarseCellSize ) );
		}
	}

	public bool MatchesField( TerrainFieldSnapshot field ) => FieldEpoch == field.Epoch &&
		EditRevision == field.GetCorrectionRange( SamplingBounds, out _, out _ );

	public GpuTransitionDescriptor WithField( TerrainFieldSnapshot field, bool captureRegion = true )
	{
		var revision = field.GetCorrectionRange( SamplingBounds, out _, out _ );
		if ( EditRevision == revision && FieldEpoch == field.Epoch && (revision == 0 || Field is not null) ) return this;
		return this with { Field = !captureRegion || revision == 0 ? null : field.CaptureRegion( SamplingBounds, pinSamples: false ), EditRevision = revision, FieldEpoch = field.Epoch };
	}

	public bool Equals( GpuTransitionDescriptor other ) => Key == other.Key &&
		CellsPerAxis == other.CellsPerAxis && FineCellSize == other.FineCellSize &&
		CoarseCellSize == other.CoarseCellSize && TerrainSettings == other.TerrainSettings &&
		GeneratorVersion == other.GeneratorVersion && SourceRevision == other.SourceRevision &&
		EditRevision == other.EditRevision && FieldEpoch == other.FieldEpoch;

	public override int GetHashCode() => System.HashCode.Combine( Key, CellsPerAxis,
		FineCellSize, CoarseCellSize, TerrainSettings, GeneratorVersion, SourceRevision, System.HashCode.Combine( EditRevision, FieldEpoch ) );
}
