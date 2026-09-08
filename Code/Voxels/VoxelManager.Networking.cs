using System;
using System.Diagnostics;

public sealed partial class VoxelManager : Component.INetworkListener
{
	private bool _terrainFingerprintBusy;
	private Guid _terrainFingerprintRequest;
	private long _terrainFingerprintStarted;
	private readonly HashSet<Guid> _terrainFingerprintRespondents = new();

	/// <summary>Compare canonical content for a bounded region across the active session.</summary>
	[ConCmd( "voxel_terrain_fingerprint" )]
	public static void TerrainFingerprintCommand( float x, float y, float z, float radius = 512f )
	{
		if ( !TryGetActiveManager( "terrain.fingerprint", out var manager ) || !Networking.IsHost ) return;
		if ( !TerrainField.IsValidBrush( new Vector3( x, y, z ), radius, 1f ) )
		{
			Log.Warning( "[TerrainNetwork] fingerprint.rejected invalid region" );
			return;
		}
		if ( manager._terrainFingerprintStarted != 0 && Stopwatch.GetElapsedTime( manager._terrainFingerprintStarted ).TotalSeconds < 10 ) return;
		manager._terrainFingerprintRequest = Guid.NewGuid();
		manager._terrainFingerprintStarted = Stopwatch.GetTimestamp();
		manager._terrainFingerprintRespondents.Clear();
		manager.InspectTerrainRegion( manager._terrainFingerprintRequest, new Vector3( x, y, z ), radius );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private async void InspectTerrainRegion( Guid request, Vector3 center, float radius )
	{
		if ( _terrainFingerprintBusy || !TerrainField.IsValidBrush( center, radius, 1f ) ) return;
		var bounds = new SdfWorldAabb( center - Vector3.One * radius, center + Vector3.One * radius );
		if ( !HasTerrainReplicaCoverage( bounds ) ) return;
		_terrainFingerprintBusy = true;
		try
		{
			var snapshot = CurrentField.CaptureRegion( bounds, pinSamples: false );
			var cancellation = _terrainEditCancellation.Token;
			var hash = await Task.RunInThreadAsync( () => TerrainFieldCodec.RegionFingerprint( snapshot, bounds, cancellation ) );
			if ( !IsValid || cancellation.IsCancellationRequested ) return;
			ReportTerrainFingerprint( request, snapshot.WorldId, snapshot.Revision, hash );
		}
		catch ( System.OperationCanceledException ) { }
		catch ( Exception exception ) { Log.Warning( $"[TerrainNetwork] fingerprint.failed reason={exception.Message}" ); }
		finally { _terrainFingerprintBusy = false; }
	}

	[Rpc.Host]
	private void ReportTerrainFingerprint( Guid request, Guid world, int revision, string hash )
	{
		if ( !Networking.IsHost || hash is null || hash.Length != 64 || request != _terrainFingerprintRequest ||
			_terrainFingerprintStarted == 0 || Stopwatch.GetElapsedTime( _terrainFingerprintStarted ).TotalSeconds > 10 ) return;
		var peer = Rpc.Caller?.Id ?? Connection.Local?.Id ?? Guid.Empty;
		if ( !_terrainFingerprintRespondents.Add( peer ) ) return;
		Log.Info( $"[TerrainNetwork] fingerprint request={request} peer={peer} world={world} revision={revision} sha256={hash}" );
	}

	private PlayerController _terrainHostPlayer;
	private Transform _terrainPlayerSpawn;
	private readonly Dictionary<Guid, GameObject> _terrainSessionPlayers = new();
	private readonly HashSet<int> _terrainSpawnSlots = new();
	private readonly Dictionary<Guid, int> _terrainConnectionSlots = new();

	[ConCmd( "voxel_terrain_network_info" )]
	public static void TerrainNetworkInfoCommand()
	{
		if ( !TryGetActiveManager( "terrain.network.inspect", out var manager ) ) return;
		Log.Info( "[TerrainNetwork] inspect " + System.Text.Json.JsonSerializer.Serialize( new
		{
			Networking.IsActive, Networking.IsHost, Local = Connection.Local?.Id,
			WorldNetworked = manager.GameObject.Network.Active,
			manager._terrainNetworkEpoch, manager._terrainReplicaEpoch,
			manager._terrainBytesSent, manager._terrainBytesReceived, manager._terrainTransfersApplied, manager._terrainNetworkFailure,
			Receiving = manager._terrainIncoming?.Id, ActiveTransfers = manager._terrainPeerOrder.Count( peer => peer.Transfer is not null ),
			Players = manager.Scene.GetAllComponents<PlayerController>().Select( player => new
			{
				player.GameObject.Id, Owner = player.GameObject.Network.OwnerId, player.IsProxy, player.Enabled,
				Position = player.WorldPosition, player.IsOnGround, player.UseInputControls, player.UseLookControls
			} ).ToArray(),
			Connections = Connection.All.Select( connection => new { connection.Id, connection.IsHost, connection.IsActive } ).ToArray()
		}, PerformanceJsonOptions ) );
	}

	private void InitializeTerrainSession()
	{
		if ( !Networking.IsHost ) return;
		_terrainHostPlayer = Scene.GetAllComponents<PlayerController>().FirstOrDefault( player => !player.IsProxy );
		if ( !_terrainHostPlayer.IsValid() ) return;
		_terrainPlayerSpawn = _terrainHostPlayer.WorldTransform;
		if ( !Networking.IsActive ) return;
		foreach ( var connection in Connection.All )
			if ( connection.IsActive ) OnActive( connection );
	}

	public void OnActive( Connection connection )
	{
		if ( !Networking.IsHost || connection is null || _terrainSessionPlayers.ContainsKey( connection.Id ) ) return;
		if ( !_terrainHostPlayer.IsValid() )
		{
			Log.Error( "[TerrainNetwork] player.spawn.failed reason=No authored host player" );
			return;
		}
		if ( !GameObject.Network.Active && !GameObject.NetworkSpawn( (Connection)null ) )
		{
			Log.Error( "[TerrainNetwork] world.spawn.failed" );
			return;
		}
		if ( connection == Connection.Local )
		{
			var player = _terrainHostPlayer.GameObject;
			var assigned = player.Network.Active ? player.Network.AssignOwnership( connection ) : player.NetworkSpawn( connection );
			if ( !assigned ) { Log.Error( "[TerrainNetwork] player.host_assignment.failed" ); return; }
			_terrainSessionPlayers.Add( connection.Id, player );
			return;
		}
		var slot = 1;
		while ( slot < MaximumTerrainCallers && _terrainSpawnSlots.Contains( slot ) ) slot++;
		if ( slot == MaximumTerrainCallers ) { connection.Kick( "The terrain session is full." ); return; }
		var spawn = _terrainPlayerSpawn;
		spawn.Position += Vector3.Right * (128f * slot) + Vector3.Up * 256f;
		var remote = GameObject.Clone( "prefabs/terrain_player.prefab", spawn, startEnabled: false );
		if ( !remote.NetworkSpawn( connection ) )
		{
			remote.Destroy();
			Log.Error( "[TerrainNetwork] player.spawn.failed reason=Network spawn rejected" );
			return;
		}
		remote.Enabled = true;
		_terrainSessionPlayers.Add( connection.Id, remote );
		_terrainConnectionSlots.Add( connection.Id, slot );
		_terrainSpawnSlots.Add( slot );
		Log.Info( $"[TerrainNetwork] player.joined connection={connection.Id} slot={slot}" );
	}

	public void OnDisconnected( Connection connection )
	{
		if ( !Networking.IsHost || connection is null ) return;
		_terrainCallers.Remove( connection.Id );
		if ( _terrainPeers.Remove( connection.Id, out var peer ) )
		{
			CancelTerrainPeerWork( peer );
			_terrainPeerOrder.Remove( peer );
		}
		if ( _terrainConnectionSlots.Remove( connection.Id, out var slot ) ) _terrainSpawnSlots.Remove( slot );
		if ( !_terrainSessionPlayers.Remove( connection.Id, out var player ) || !player.IsValid() ||
			player == _terrainHostPlayer?.GameObject ) return;
		player.Destroy();
	}

	private readonly List<Vector3Int> _terrainActorInterests = new();
	private readonly HashSet<Vector3Int> _terrainActorInterestSet = new();
	private bool _terrainActorCapacityWarning;

	private bool RecordTerrainActorInterest( BBox bounds )
	{
		if ( !float.IsFinite( bounds.Mins.x ) || !float.IsFinite( bounds.Mins.y ) || !float.IsFinite( bounds.Mins.z ) ||
			!float.IsFinite( bounds.Maxs.x ) || !float.IsFinite( bounds.Maxs.y ) || !float.IsFinite( bounds.Maxs.z ) ||
			Math.Abs( bounds.Mins.x ) > TerrainField.MaximumWorldCoordinate || Math.Abs( bounds.Mins.y ) > TerrainField.MaximumWorldCoordinate ||
			Math.Abs( bounds.Mins.z ) > TerrainField.MaximumWorldCoordinate || Math.Abs( bounds.Maxs.x ) > TerrainField.MaximumWorldCoordinate ||
			Math.Abs( bounds.Maxs.y ) > TerrainField.MaximumWorldCoordinate || Math.Abs( bounds.Maxs.z ) > TerrainField.MaximumWorldCoordinate ) return false;
		var min = WorldToChunkCoordinate( bounds.Mins );
		var max = WorldToChunkCoordinate( bounds.Maxs );
		var volume = ((long)max.x - min.x + 1) * ((long)max.y - min.y + 1) * ((long)max.z - min.z + 1);
		if ( volume < 1 || volume > VoxelCollisionWorld.MaximumActorRegions ) return false;
		for ( var z = min.z; z <= max.z; z++ )
		for ( var y = min.y; y <= max.y; y++ )
		for ( var x = min.x; x <= max.x; x++ )
		{
			var coordinate = new Vector3Int( x, y, z );
			var covered = false;
			foreach ( var center in _terrainPlayerInterests )
			{
				var delta = coordinate - center;
				if ( Math.Abs( delta.x ) <= _appliedGameplayRadius && Math.Abs( delta.y ) <= _appliedGameplayRadius &&
					Math.Abs( delta.z ) <= _appliedGameplayRadius ) { covered = true; break; }
			}
			if ( covered || _terrainActorInterestSet.Contains( coordinate ) ) continue;
			if ( _terrainActorInterestSet.Count == VoxelCollisionWorld.MaximumActorRegions ) return false;
			_terrainActorInterestSet.Add( coordinate );
		}
		return true;
	}

	private readonly List<Vector3Int> _terrainPlayerInterests = new();

	private void UpdatePlayerCollisionInterests()
	{
		if ( _collision is null ) return;
		_terrainPlayerInterests.Clear();
		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
		{
			if ( !player.Enabled || (!Networking.IsHost && player.IsProxy) ) continue;
			var coordinate = WorldToChunkCoordinate( player.WorldPosition );
			if ( _terrainPlayerInterests.Contains( coordinate ) ) continue;
			if ( _terrainPlayerInterests.Count == VoxelCollisionWorld.MaximumPlayerInterests ) break;
			_terrainPlayerInterests.Add( coordinate );
		}
		if ( _terrainPlayerInterests.Count == 0 ) _terrainPlayerInterests.Add( _streamingCenterCoordinate );
		_terrainPlayerInterests.Sort( ( a, b ) =>
		{
			var order = a.z.CompareTo( b.z );
			if ( order != 0 ) return order;
			order = a.y.CompareTo( b.y );
			return order != 0 ? order : a.x.CompareTo( b.x );
		} );
		_collision.SetInterest( _terrainPlayerInterests, _terrainActorInterests, _appliedGameplayRadius, _terrainContentRevision, CurrentField );
	}

	private Guid _terrainNetworkEpoch = Guid.NewGuid();
	private Guid _terrainReplicaEpoch = Guid.Empty;
	private long _terrainClientSequence;
	private long _terrainLastAdmissionSequence;
	private bool _terrainLastAdmissionAccepted;
	private long _terrainLastAdmissionRequestId;
	private readonly Dictionary<Guid, TerrainCallerState> _terrainCallers = new();
	private readonly List<Guid> _terrainDisconnectedCallers = new();
	private const int MaximumTerrainCallers = 64;

	private sealed class TerrainCallerState
	{
		public long Sequence;
		public long LastAttempt;
		public double Tokens = 2;
		public long RequestId;
		public bool Accepted;
	}

	// Replica readiness is established only by coherent host state reception.
	private void SendTerrainToolRequest( Vector3 direction, bool build )
	{
		if ( _terrainReplicaEpoch == Guid.Empty ) return;
		RequestTerrainTool( _terrainReplicaEpoch, checked( ++_terrainClientSequence ), direction, build );
	}

	[Rpc.Host]
	private void RequestTerrainTool( Guid epoch, long sequence, Vector3 direction, bool build )
	{
		var caller = Rpc.Caller;
		if ( !Networking.IsHost || caller is null || epoch != _terrainNetworkEpoch || sequence < 1 ) return;
		if ( !_terrainPeers.TryGetValue( caller.Id, out var peer ) || !peer.Ready || !peer.Player.IsValid() ||
			!TerrainReplicationManifest.Contains( peer.Coverage, new SdfWorldAabb( peer.Player.WorldPosition, peer.Player.WorldPosition ) ) ) return;
		if ( !_terrainCallers.TryGetValue( caller.Id, out var state ) )
		{
			_terrainDisconnectedCallers.Clear();
			foreach ( var id in _terrainCallers.Keys )
				if ( !Connection.All.Any( connection => connection.Id == id ) ) _terrainDisconnectedCallers.Add( id );
			foreach ( var id in _terrainDisconnectedCallers ) _terrainCallers.Remove( id );
			if ( _terrainCallers.Count >= MaximumTerrainCallers ) return;
			state = new TerrainCallerState();
			_terrainCallers.Add( caller.Id, state );
		}
		// Reliable admission delivery already carries the result; duplicate input never
		// mutates again or amplifies outbound traffic.
		if ( sequence <= state.Sequence ) return;
		state.Sequence = sequence;
		state.Accepted = false;
		state.RequestId = 0;
		var now = Stopwatch.GetTimestamp();
		// Two-token burst tolerance avoids rejecting a valid 10 Hz stream for arrival jitter.
		if ( state.LastAttempt != 0 ) state.Tokens = Math.Min( 2,
			state.Tokens + Stopwatch.GetElapsedTime( state.LastAttempt, now ).TotalSeconds / TerrainToolTickSeconds );
		state.LastAttempt = now;
		if ( state.Tokens >= 1 )
		{
			state.Tokens--;
			var player = Scene.GetAllComponents<PlayerController>().FirstOrDefault( value => value.GameObject.Network.OwnerId == caller.Id );
			// Origin, hit, reach, brush size and strength all come from host state/rules.
			// No client-provided hit position or density value is trusted.
			if ( player.IsValid() && player.Enabled && _deformationBenchmark is null )
				state.Accepted = TryUseTerrainTool( player, direction, build, out state.RequestId );
		}
		using ( Rpc.FilterInclude( caller ) ) TerrainToolAdmission( epoch, sequence, state.Accepted, state.RequestId );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void TerrainToolAdmission( Guid epoch, long sequence, bool accepted, long requestId )
	{
		if ( Networking.IsHost || epoch != _terrainReplicaEpoch || sequence <= _terrainLastAdmissionSequence ) return;
		_terrainLastAdmissionSequence = sequence;
		_terrainLastAdmissionAccepted = accepted;
		_terrainLastAdmissionRequestId = requestId;
	}
}
