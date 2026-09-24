using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using System.Collections.Generic;

/// <summary>One terrain-supported population of shared Blender models and solid trunks.</summary>
internal sealed class SpawnTreePopulation : IDisposable
{
	public const int Capacity = 512;
	public const float RadiusMeters = 240f;
	public const float RenderMeters = 900f;
	public const float ShadowMeters = 65f;
	private const float DetailReturnMeters = 100f;
	private const float DetailExitMeters = 110f;
	private const float DetailTransitionSeconds = 0.35f;
	private const float UnitsPerMeter = 1f / 0.0254f;
	private const float SpacingMeters = 18f;
	private const int GridHalf = 13;
	private const int GridSide = GridHalf * 2 + 1;
	private const int Candidates = GridSide * GridSide;
	private const int PlacementBatch = 8;
	private TreeSpecimen[] _specimens;
	private int[][] _choices;
	private readonly Scene _scene;
	private readonly SceneWorld _world;
	private readonly Vector3 _anchor;
	private readonly CancellationTokenSource _cancellation = new();
	private Model[] _models;
	private Model[] _farModels;
	private readonly SceneObject[] _farObjects = new SceneObject[Candidates];
	private readonly float[] _detailFade = new float[Candidates];
	private readonly bool[] _farSelected = new bool[Candidates];
	private readonly SceneObject[] _objects = new SceneObject[Candidates];
	private readonly GameObject[] _trunks = new GameObject[Candidates];
	private readonly int[] _variantIds = new int[Candidates];
	private readonly SdfWorldAabb[] _supportBounds = new SdfWorldAabb[Candidates];
	private int _modelCount;
	private Task<Placement[]> _placementTask;
	private int _sweepRevision;
	private int _sweepEpoch;
	private Placement[] _placements;
	private int _placementCursor;
	private int _candidate;
	private int _completedRevision = -1;
	private int _completedEpoch = -1;
	private bool _retryPages;
	private float _retryElapsed;
	private float _lodElapsed;
	private bool _disposed;
	private bool _failed;
	public int Count { get; private set; }
	public int VisibleCount { get; private set; }
	public int ShadowCount { get; private set; }
	public int DetailCount { get; private set; }
	public int FarCount { get; private set; }
	public int CrossfadeCount { get; private set; }
	public int ImportedModels => _modelCount;
	public long GeometryVertices { get; private set; }
	public long GeometryIndices { get; private set; }
	public int GeometryMeshes { get; private set; }
	public double PeakUpdateMilliseconds { get; private set; }
	public double PeakLoadMilliseconds { get; private set; }
	public int StaleBatches { get; private set; }
	public string Status => _failed ? "Failed; see console" : _specimens is null ? "Waiting for tree catalog"
		: _modelCount < _specimens.Length ? $"Loading Blender models {_modelCount}/{_specimens.Length}"
		: _completedRevision < 0 ? "Finding grassland support" : "Blender trees ready";
	private readonly record struct Placement( int Candidate, uint Seed, int Variant, Vector3 Position, bool Accepted, bool Retry,
		SdfWorldAabb Bounds = default, int Revision = -1, int Epoch = 0 );

	private sealed class ExportManifest
	{
		[JsonPropertyName( "lods" )] public ExportLod[] Lods { get; set; }
	}
	private sealed class TreeCatalog
	{
		public int Version { get; set; }
		public TreeSpecimen[] Specimens { get; set; }
	}
	private sealed class TreeSpecimen
	{
		public string Key { get; set; }
		public string Species { get; set; }
		public string Stage { get; set; }
		public string Form { get; set; }
		public int Seed { get; set; }
		[JsonIgnore] public int SpeciesIndex { get; set; }
	}
	private sealed class ExportLod
	{
		[JsonPropertyName( "bounds_inches" )] public float[][] Bounds { get; set; }
	}

	public SpawnTreePopulation( Scene scene, Vector3 anchor )
	{
		_scene = scene;
		_world = scene.SceneWorld;
		_anchor = anchor.WithZ( 0f );
	}

	public void Update( TerrainFieldSnapshot field, Vector3 viewer, bool ready, SdfWorldAabb? replicaCoverage )
	{
		if ( _disposed || _failed ) return;
		var started = Stopwatch.GetTimestamp();
		try
		{
			using var scope = global::Sandbox.Diagnostics.Performance.Scope( "Trees.Update" );
			if ( _specimens is null || _modelCount < _specimens.Length ) AdvanceLibrary();
			else if ( ready ) AdvancePlacement( field, replicaCoverage );
			_lodElapsed += RealTime.Delta;
			if ( _lodElapsed >= (CrossfadeCount > 0 ? 0f : 0.1f) || !ready )
			{
				var fadeStep = Math.Min( _lodElapsed / DetailTransitionSeconds, 1f );
				_lodElapsed = 0f;
				VisibleCount = 0;
				ShadowCount = 0;
				DetailCount = 0;
				FarCount = 0;
				CrossfadeCount = 0;
				for ( var index = 0; index < _objects.Length; index++ )
				{
					var tree = _objects[index];
					if ( tree is null ) continue;
					var distance = (tree.Position - viewer).Length / UnitsPerMeter;
					var supported = ready && (!replicaCoverage.HasValue || TerrainReplicationManifest.Contains( replicaCoverage.Value, _supportBounds[index] ));
					var visible = supported && distance < RenderMeters;
					// Distance selects a representation; time completes the fade even
					// when the viewer stops. Hysteresis prevents boundary oscillation.
					var scaledDistance = distance / tree.Transform.Scale.x;
					var detail = TreeLodTransition.Advance( _detailFade[index], ref _farSelected[index],
						scaledDistance, DetailReturnMeters, DetailExitMeters, fadeStep,
						Math.Min( RealTime.Delta / DetailTransitionSeconds, 1f ) );
					var far = _farObjects[index];
					if ( _detailFade[index] != detail )
					{
						_detailFade[index] = detail;
						tree.Attributes.Set( "TreeLodFade", detail );
						far.Attributes.Set( "TreeLodFade", detail );
					}
					var detailed = visible && detail > 0f;
					var distant = visible && detail < 1f;
					if ( tree.RenderingEnabled != detailed ) tree.RenderingEnabled = detailed;
					if ( far.RenderingEnabled != distant ) far.RenderingEnabled = distant;
					if ( detailed ) DetailCount++;
					if ( distant ) FarCount++;
					if ( detailed && distant ) CrossfadeCount++;
					if ( _trunks[index].Enabled != supported ) _trunks[index].Enabled = supported;
					if ( !visible ) continue;
					// Native mesh LODs remain automatic inside the near representation; physics never switches.
					var shadows = distance < ShadowMeters;
					if ( tree.Flags.CastShadows != shadows ) tree.Flags.CastShadows = shadows;
					VisibleCount++;
					if ( shadows ) ShadowCount++;
				}
			}
		}
		catch ( Exception exception )
		{
			_failed = true;
			Log.Error( $"[SpawnTrees] failed {exception}" );
		}
		finally { PeakUpdateMilliseconds = Math.Max( PeakUpdateMilliseconds, Stopwatch.GetElapsedTime( started ).TotalMilliseconds ); }
	}

	private void AdvanceLibrary()
	{
		if ( _specimens is null )
		{
			if ( !FileSystem.Mounted.FileExists( "models/tree_lab/catalog.json" ) ) return;
			var catalog = FileSystem.Mounted.ReadJson<TreeCatalog>( "models/tree_lab/catalog.json" );
			if ( catalog?.Version != 1 || catalog.Specimens is null || catalog.Specimens.Length is < 4 or > 128 )
				throw new InvalidOperationException( "Invalid imported tree catalog" );
			var choices = new List<int>[12];
			for ( var i = 0; i < choices.Length; i++ ) choices[i] = new();
			var keys = new HashSet<string>( StringComparer.Ordinal );
			for ( var i = 0; i < catalog.Specimens.Length; i++ )
			{
				var specimen = catalog.Specimens[i];
				if ( string.IsNullOrEmpty( specimen.Key ) || specimen.Key.Contains( '/' ) || specimen.Key.Contains( '\\' ) ||
					specimen.Key.Contains( ".." ) || !keys.Add( specimen.Key ) )
					throw new InvalidOperationException( "Invalid or duplicate tree asset key" );
				specimen.SpeciesIndex = specimen.Species switch { "Oak" => 0, "Ash" => 1, "Spruce" => 2, "Birch" => 3, _ => -1 };
				var stage = specimen.Stage switch { "Juvenile" => 0, "Mature" => 1, "Large" => 2, _ => -1 };
				if ( specimen.SpeciesIndex < 0 || stage < 0 ) throw new InvalidOperationException( "Unknown tree species or stage" );
				choices[specimen.SpeciesIndex * 3 + stage].Add( i );
			}
			for ( var species = 0; species < 4; species++ )
				if ( choices[species * 3].Count == 0 || choices[species * 3 + 1].Count == 0 )
					throw new InvalidOperationException( "Each tree species requires juvenile and mature models" );
			_choices = new int[12][];
			for ( var i = 0; i < choices.Length; i++ ) _choices[i] = choices[i].ToArray();
			_specimens = catalog.Specimens;
			_models = new Model[_specimens.Length];
			_farModels = new Model[_specimens.Length];
			Log.Info( $"[SpawnTrees] catalog={_specimens.Length} seeded source shapes" );
		}
		var key = _specimens[_modelCount].Key;
		var folder = $"models/tree_lab/{key}";
		if ( !FileSystem.Mounted.FileExists( $"{folder}/manifest.json" ) ) return;
		var started = Stopwatch.GetTimestamp();
		var model = Model.Load( $"{folder}/{key}.vmdl" );
		if ( model is null || model.IsError ) return;
		var manifest = FileSystem.Mounted.ReadJson<ExportManifest>( $"{folder}/manifest.json" );
		if ( manifest?.Lods?.Length != 3 )
			throw new InvalidOperationException( $"Tree manifest requires three LODs: {key}" );
		var minimum = new Vector3( float.MaxValue );
		var maximum = new Vector3( float.MinValue );
		foreach ( var lod in manifest.Lods )
		{
			if ( lod?.Bounds?.Length != 2 || lod.Bounds[0]?.Length != 3 || lod.Bounds[1]?.Length != 3 )
				throw new InvalidOperationException( $"Invalid exported tree bounds: {key}" );
			for ( var axis = 0; axis < 3; axis++ )
				if ( !float.IsFinite( lod.Bounds[0][axis] ) || !float.IsFinite( lod.Bounds[1][axis] ) || lod.Bounds[0][axis] > lod.Bounds[1][axis] )
					throw new InvalidOperationException( $"Nonfinite or unordered exported tree bounds: {key}" );
			minimum = Vector3.Min( minimum, new Vector3( lod.Bounds[0][0], lod.Bounds[0][1], lod.Bounds[0][2] ) );
			maximum = Vector3.Max( maximum, new Vector3( lod.Bounds[1][0], lod.Bounds[1][1], lod.Bounds[1][2] ) );
		}
		for ( var axis = 0; axis < 3; axis++ )
			if ( !float.IsFinite( model.Bounds.Mins[axis] ) || !float.IsFinite( model.Bounds.Maxs[axis] ) || model.Bounds.Mins[axis] > model.Bounds.Maxs[axis] )
				throw new InvalidOperationException( $"Nonfinite or unordered compiled tree bounds: {key}" );
		Log.Info( $"[SpawnTrees] imported={key} bounds={model.Bounds} expectedMin={minimum} expectedMax={maximum} lods={model.MeshInfo.LodCount} switches={string.Join( ",", model.MeshInfo.LodSwitchDistances )} physics={model.Physics.IsValid}" );
		if ( (model.Bounds.Mins - minimum).Length > 2f || (model.Bounds.Maxs - maximum).Length > 2f )
			throw new InvalidOperationException( $"Imported tree bounds differ from inch export: {key}" );
		if ( model.MeshInfo.LodCount != 3 || !model.Physics.IsValid )
			throw new InvalidOperationException( $"Tree requires three LODs and trunk physics: {key}" );
		var distant = TreeDistantModel.Load( key, model.Bounds, new[] { model }, out _ );
		if ( distant is null ) return;
		_farModels[_modelCount] = distant;
		_models[_modelCount++] = model;
		GeometryVertices += model.MeshInfo.TotalVertices;
		GeometryIndices += model.MeshInfo.TotalTriangles * 3L;
		GeometryMeshes += model.MeshCount;
		PeakLoadMilliseconds = Math.Max( PeakLoadMilliseconds, Stopwatch.GetElapsedTime( started ).TotalMilliseconds );
	}

	private static uint Hash( uint value )
	{
		value ^= value >> 16; value *= 0x7feb352du; value ^= value >> 15; value *= 0x846ca68bu;
		return value ^ (value >> 16);
	}
	private static float Unit( uint seed, uint channel ) => (Hash( seed ^ (channel * 0x9e3779b9u) ) >> 8) / 16777216f;

	private void AdvancePlacement( TerrainFieldSnapshot field, SdfWorldAabb? replicaCoverage )
	{
		if ( _placements is not null )
		{
			// Bound creation/destruction too; generation timing does not hide scene costs.
			for ( var integrated = 0; integrated < 2 && _placementCursor < _placements.Length; integrated++ )
			{
				var placement = _placements[_placementCursor++];
				if ( placement.Revision >= 0 && replicaCoverage.HasValue &&
					!TerrainReplicationManifest.Contains( replicaCoverage.Value, placement.Bounds ) )
				{
					_retryPages = true;
					continue;
				}
				// Reject only stale spatial dependencies. Unrelated edits cannot restart
				// the cursor and permanently starve trees at the end of the population.
				if ( placement.Revision >= 0 && (placement.Epoch != field.Epoch ||
					field.GetCorrectionRange( placement.Bounds, out _, out _ ) != placement.Revision) )
				{
					StaleBatches++;
					_retryPages = true;
					continue;
				}
				_retryPages |= placement.Retry;
				if ( placement.Retry ) continue;
				var previous = _objects[placement.Candidate];
				if ( !placement.Accepted )
				{
					if ( previous is not null ) { previous.Delete(); _objects[placement.Candidate] = null; _farObjects[placement.Candidate]?.Delete(); _farObjects[placement.Candidate] = null; _trunks[placement.Candidate]?.Destroy(); _trunks[placement.Candidate] = null; Count--; }
					continue;
				}
				if ( previous is null && Count >= Capacity ) continue;
				var scale = 0.78f + Unit( placement.Seed, 301 ) * 0.44f;
				var rotation = Rotation.FromYaw( Unit( placement.Seed, 302 ) * 360f );
				var transform = new Transform( placement.Position, rotation, scale );
				if ( previous is null )
				{
					previous = new SceneObject( _world, _models[placement.Variant], transform );
					// Each instance supplies its own fade to both rendering passes.
					previous.Batchable = false;
					previous.ColorTint = new Color( 0.94f + Unit( placement.Seed, 303 ) * 0.06f,
						0.94f + Unit( placement.Seed, 304 ) * 0.06f, 0.92f, 1f );

					var far = new SceneObject( _world, _farModels[placement.Variant], transform );
					far.Batchable = false;
					far.ColorTint = previous.ColorTint;
					far.RenderingEnabled = false;
					far.Flags.CastShadows = false;
					far.Attributes.Set( "TreeLodFade", 1f );
					_farObjects[placement.Candidate] = far;
					_detailFade[placement.Candidate] = float.NaN;
					previous.RenderingEnabled = false;
					previous.Flags.CastShadows = false;
					_objects[placement.Candidate] = previous;
					_variantIds[placement.Candidate] = placement.Variant;
					using ( _scene.Push() )
					{
						var trunk = new GameObject( false, $"Tree Lab {_specimens[placement.Variant].Key} [{placement.Candidate}]" );
						trunk.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
						trunk.WorldTransform = transform;
						trunk.Tags.Add( "tree_trunk" );
						var collider = trunk.Components.Create<ModelCollider>();
						collider.Model = _models[placement.Variant];
						collider.Static = true;
						collider.IsTrigger = false;
						trunk.Enabled = true;
						_trunks[placement.Candidate] = trunk;
					}
					Count++;
				}
				else if ( previous.Transform != transform )
				{
					previous.Transform = transform;
					_farObjects[placement.Candidate].Transform = transform;
					_trunks[placement.Candidate].WorldTransform = transform;
				}
				// SceneObject bounds are the world AABB. Reserve the complete bounded
				// vertex deformation, rather than culling against the static leaf pose.
				var local = _models[placement.Variant].Bounds;
				var margin = local.Size.Length * 0.12f + 8f;
				var minimum = new Vector3( float.MaxValue );
				var maximum = new Vector3( float.MinValue );
				for ( var corner = 0; corner < 8; corner++ )
				{
					var point = new Vector3( (corner & 1) == 0 ? local.Mins.x - margin : local.Maxs.x + margin,
						(corner & 2) == 0 ? local.Mins.y - margin : local.Maxs.y + margin,
						(corner & 4) == 0 ? local.Mins.z - margin : local.Maxs.z + margin );
					point = transform.Position + transform.Rotation * (point * transform.Scale);
					minimum = Vector3.Min( minimum, point ); maximum = Vector3.Max( maximum, point );
				}
				previous.Bounds = new BBox( minimum, maximum );
				_supportBounds[placement.Candidate] = placement.Bounds;
			}
			if ( _placementCursor < _placements.Length ) return;
			_candidate += _placements.Length;
			_placements = null;
			if ( _candidate >= Candidates )
			{
				_completedRevision = _sweepRevision;
				_completedEpoch = _sweepEpoch;
				_candidate = 0;
				_retryElapsed = 0f;
				Log.Info( $"[SpawnTrees] population={Count} revision={field.Revision} retryPages={_retryPages} vertices={GeometryVertices} indices={GeometryIndices}" );
				return;
			}
		}
		if ( _placementTask is not null )
		{
			if ( !_placementTask.IsCompleted ) return;
			_placements = _placementTask.GetAwaiter().GetResult();
			_placementTask = null;
			_placementCursor = 0;
			return;
		}
		if ( _candidate == 0 && _completedRevision == field.Revision && _completedEpoch == field.Epoch )
		{
			_retryElapsed += RealTime.Delta;
			if ( !_retryPages || _retryElapsed < 2f ) return;
			_retryPages = false;
		}
		if ( _candidate == 0 )
		{
			_sweepRevision = field.Revision;
			_sweepEpoch = field.Epoch;
		}
		var start = _candidate;
		var anchor = _anchor;
		var token = _cancellation.Token;
		var choices = _choices;
		_placementTask = GameTask.RunInThreadAsync( () => FindPlacements( field, anchor, start, choices, token ) );
	}

	private static Placement[] FindPlacements( TerrainFieldSnapshot field, Vector3 anchor, int start, int[][] choices, CancellationToken token )
	{
		var result = new Placement[Math.Min( PlacementBatch, Candidates - start )];
		for ( var offset = 0; offset < result.Length; offset++ )
		{
			token.ThrowIfCancellationRequested();
			var index = start + offset;
			var x = index % GridSide - GridHalf;
			var y = index / GridSide - GridHalf;
			var seed = Hash( unchecked((uint)field.Settings.WorldSeed) ^
				unchecked((uint)x * 1597334677u) ^ unchecked((uint)y * 3812015801u) ^ 0x74524545u );
			var position = anchor + new Vector3( x + (Unit( seed, 1 ) - 0.5f) * 0.6f,
				y + (Unit( seed, 2 ) - 0.5f) * 0.6f, 0f ) * SpacingMeters * UnitsPerMeter;
			var distance = (position - anchor).Length / UnitsPerMeter;
			var species = Math.Min( 3, (int)(Unit( seed, 4 ) * 4) );
			var age = Unit( seed, 5 );
			var stage = age < 0.22f ? 0 : age > 0.88f && choices[species * 3 + 2].Length > 0 ? 2 : 1;
			var variants = choices[species * 3 + stage];
			var variant = variants[Math.Min( variants.Length - 1, (int)(Unit( seed, 6 ) * variants.Length) )];
			result[offset] = new( index, seed, variant, position, false, false );
			if ( distance < 14f || distance > RadiusMeters || Unit( seed, 3 ) > 0.72f ) continue;
			var land = RegionalLandforms.SampleWorld( position, field.Settings );
			if ( land.Mountains > 0.35f || land.Height < field.Settings.SeaLevel + 32f ) continue;
			position.z = land.Height;
			var extent = new Vector3( 128f, 128f, 160f );
			var bounds = new SdfWorldAabb( position - extent, position + extent );
			result[offset] = result[offset] with { Bounds = bounds, Revision = field.GetCorrectionRange( bounds, out _, out _ ), Epoch = field.Epoch };
			if ( !field.TryCaptureRegion( bounds, out var reader ) )
			{
				result[offset] = result[offset] with { Retry = true };
				continue;
			}
			// A narrow bracket rejects caves, large excavations and ambiguous support.
			var low = land.Height - 64f;
			var high = land.Height + 64f;
			if ( reader.SampleWorld( position.WithZ( low ) ) >= 0f || reader.SampleWorld( position.WithZ( high ) ) <= 0f ) continue;
			for ( var iteration = 0; iteration < 10; iteration++ )
			{
				var middle = (low + high) * 0.5f;
				if ( reader.SampleWorld( position.WithZ( middle ) ) <= 0f ) low = middle;
				else high = middle;
			}
			position.z = (low + high) * 0.5f;
			if ( !ProceduralVoxelMaterials.TrySample( reader, position - Vector3.Up * 2f, out _, out var material ) || material != VoxelMaterials.Grass ) continue;
			var delta = 12f;
			var gradient = new Vector3(
				reader.SampleWorld( position + Vector3.Forward * delta ) - reader.SampleWorld( position - Vector3.Forward * delta ),
				reader.SampleWorld( position + Vector3.Right * delta ) - reader.SampleWorld( position - Vector3.Right * delta ),
				reader.SampleWorld( position + Vector3.Up * delta ) - reader.SampleWorld( position - Vector3.Up * delta ) ).Normal;
			if ( gradient.z < 0.9f ) continue;
			var supported = true;
			for ( var corner = 0; corner < 4; corner++ )
			{
				var probe = position + new Vector3( (corner & 1) == 0 ? -20f : 20f,
					(corner & 2) == 0 ? -20f : 20f, -24f );
				if ( reader.SampleWorld( probe ) >= 0f ) supported = false;
			}
			if ( supported ) result[offset] = result[offset] with { Position = position - Vector3.Up * 4f, Accepted = true };
		}
		return result;
	}


	public void LogNearestTrees( Vector3 viewer, int candidate = -1, int variant = -1 )
	{
		if ( _specimens is null ) return;
		for ( var model = 0; model < _specimens.Length; model++ )
		{
			var placed = 0;
			for ( var index = 0; index < _objects.Length; index++ )
				if ( _objects[index] is not null && _variantIds[index] == model ) placed++;
			Log.Info( $"[SpawnTrees] variant={model} source={_specimens[model].Key} placed={placed}" );
		}
		for ( var species = 0; species < 4; species++ )
		{
			var nearest = -1;
			var distance = float.MaxValue;
			for ( var index = 0; index < _objects.Length; index++ )
			{
				if ( _objects[index] is null || (candidate >= 0 && candidate != index) || (variant >= 0 && variant != _variantIds[index]) ) continue;
				if ( _specimens[_variantIds[index]].SpeciesIndex != species ) continue;
				var next = (_objects[index].Position - viewer).Length;
				if ( next < distance ) { distance = next; nearest = index; }
			}
			if ( nearest < 0 ) continue;
			Log.Info( $"[SpawnTrees] candidate={nearest} specimen={_specimens[_variantIds[nearest]].Key} root={_objects[nearest].Position} scale={_objects[nearest].Transform.Scale} lodFade={_detailFade[nearest]:0.###} distanceMeters={distance / UnitsPerMeter:0.##} trunkObject={_trunks[nearest].Id} detailBounds={_objects[nearest].Bounds} farBounds={_farObjects[nearest].Bounds}" );
		}
	}

	public void Dispose()
	{
		_disposed = true;
		_cancellation.Cancel();
		foreach ( var tree in _objects ) tree?.Delete();
		foreach ( var tree in _farObjects ) tree?.Delete();
		foreach ( var trunk in _trunks ) trunk?.Destroy();
		Array.Clear( _objects ); Array.Clear( _farObjects ); Array.Clear( _trunks );
		if ( _models is not null ) Array.Clear( _models );
		if ( _farModels is not null ) Array.Clear( _farModels );
		_placements = null;
		if ( _placementTask is not null ) _ = ObserveCompletion( _placementTask );
		_cancellation.Dispose();
	}
	private static async Task ObserveCompletion( Task task )
	{
		try { await task; }
		catch ( OperationCanceledException ) { }
		catch ( Exception exception ) { Log.Warning( $"[SpawnTrees] retired worker: {exception.Message}" ); }
	}
}
