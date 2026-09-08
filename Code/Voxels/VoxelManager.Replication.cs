using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

public sealed partial class VoxelManager
{
	private const int TerrainFragmentBytes = 16384;
	private const int TerrainPageWindow = 8;
	private const int MaximumActiveTerrainTransfers = 2;
	private const double TerrainPeerBytesPerSecond = 2 * 1024 * 1024;
	private const double TerrainGlobalBytesPerSecond = 4 * 1024 * 1024;
	private const double TerrainTransferTimeoutSeconds = 180;
	private readonly Dictionary<Guid, TerrainPeer> _terrainPeers = new();
	private readonly List<TerrainPeer> _terrainPeerOrder = new();
	private int _terrainSendCursor;
	private long _nextTerrainTransferId;
	private Guid _terrainReplicationWorld;
	private int _terrainReplicationFieldEpoch;
	private long _terrainSendTimestamp;
	private double _terrainSendCredit;
	private long _terrainBytesSent;
	private long _terrainBytesReceived;
	private long _terrainTransfersApplied;
	private string _terrainNetworkFailure;
	private bool _terrainAuthorityLost;
	private Guid _terrainReceivingEpoch;
	private readonly HashSet<Guid> _terrainRetiredEpochs = new();
	private long _terrainLastAppliedTransfer;
	private SdfWorldAabb _terrainReplicaCoverage;
	private Dictionary<Vector3Int, int> _terrainReplicaVersions = new();
	private TerrainIncoming _terrainIncoming;
	private PlayerController _terrainLocalPlayer;
	private GameObject _terrainObservedStreamingTarget;
	private bool _terrainUseLocalStreamingTarget;

	private sealed class TerrainPeer
	{
		public Connection Connection;
		public GameObject Player;
		public Dictionary<Vector3Int, int> Known = new();
		public SdfWorldAabb Coverage;
		public SdfWorldAabb ObservedCoverage;
		public int ObservedRevision = -1;
		public long LastCheck;
		public bool Ready;
		public int Failures;
		public long LastCredit;
		public double Credit = 4 * TerrainFragmentBytes;
		public CancellationTokenSource WorkCancellation;
		public System.Threading.Tasks.Task<TerrainOutgoing> Preparing;
		public TerrainOutgoing Transfer;
	}

	private sealed class TerrainOutgoing
	{
		public long Id;
		public long Started;
		public TerrainReplicationManifest Manifest;
		public readonly List<TerrainFieldPage> Pages = new();
		public byte[] Buffer;
		public int Block = -1;
		public int Offset;
		public int SentPages;
		public int AcknowledgedPages;
		public bool EndSent;
		public System.Threading.Tasks.Task<byte[]> Encoding;
	}

	private sealed class TerrainIncoming
	{
		public Guid Epoch;
		public CancellationTokenSource WorkCancellation;
		public long Id;
		public long Started;
		public TerrainReplicationManifest Manifest;
		public readonly Dictionary<Vector3Int, TerrainFieldPage> Pages = new();
		public byte[] Block;
		public int Offset;
		public int ReceivedPages;
		public int DecodedPages;
		public readonly Queue<byte[]> Encoded = new();
		public System.Threading.Tasks.Task<(Vector3Int Coordinate, TerrainFieldPage Page)> Decoding;
		public bool EndReceived;
		public bool Applying;
		public bool Committed;
		public bool FieldCommitted;
		public long RequestId;
	}

	private SdfWorldAabb TerrainCoverage( Vector3 position, bool padded )
	{
		var chunkSize = _appliedCellsPerAxis * _appliedCellSize;
		var coarse = 1 << _appliedVisualConfiguration.MaximumVisualLod;
		var radius = (VisualChunkRadius + coarse * (padded ? 2 : 1) + _appliedGameplayRadius + 2) * chunkSize;
		if ( padded )
		{
			var grid = coarse * chunkSize;
			position = new Vector3( MathF.Floor( position.x / grid ) * grid,
				MathF.Floor( position.y / grid ) * grid, MathF.Floor( position.z / grid ) * grid );
			// Grid bias must fit inside the padded coverage even at the smallest visual tier.
			radius += grid;
		}
		var limit = TerrainField.MaximumWorldCoordinate;
		return new SdfWorldAabb(
			new Vector3( Math.Max( -limit, position.x - radius ), Math.Max( -limit, position.y - radius ), Math.Max( -limit, position.z - radius ) ),
			new Vector3( Math.Min( limit, position.x + radius ), Math.Min( limit, position.y + radius ), Math.Min( limit, position.z + radius ) ) );
	}

	private bool HasTerrainReplicaCoverage( SdfWorldAabb bounds ) => !_terrainAuthorityLost &&
		(Networking.IsHost || _terrainReplicaEpoch != Guid.Empty && TerrainReplicationManifest.Contains( _terrainReplicaCoverage, bounds ));

	private static TerrainOutgoing PrepareTerrainTransfer( TerrainFieldSnapshot source, SdfWorldAabb coverage,
		Dictionary<Vector3Int, int> known, bool alreadyReady, SdfWorldAabb previousCoverage, CancellationToken cancellation )
	{
		var manifest = new TerrainReplicationManifest { WorldId = source.WorldId, Revision = source.Revision,
			Settings = source.Settings, Coverage = coverage };
		var outgoing = new TerrainOutgoing { Manifest = manifest };
		var pageSize = TerrainField.SampleSpacing * TerrainField.SamplesPerPageAxis;
		foreach ( var pair in source.Pages.OrderBy( entry => entry.Key.z ).ThenBy( entry => entry.Key.y ).ThenBy( entry => entry.Key.x ) )
		{
			cancellation.ThrowIfCancellationRequested();
			var origin = new Vector3( pair.Key.x, pair.Key.y, pair.Key.z ) * pageSize;
			if ( !TerrainFieldChange.Intersects( coverage, new SdfWorldAabb( origin, origin + Vector3.One * pageSize ) ) ) continue;
			manifest.Versions.Add( pair.Key, pair.Value.Revision );
			if ( known.TryGetValue( pair.Key, out var revision ) && revision == pair.Value.Revision ) continue;
			manifest.Changed.Add( pair.Key );
			outgoing.Pages.Add( pair.Value );
		}
		if ( alreadyReady && coverage == previousCoverage && manifest.Changed.Count == 0 && manifest.Versions.Count == known.Count ) return null;
		cancellation.ThrowIfCancellationRequested();
		outgoing.Buffer = manifest.Encode();
		return outgoing;
	}

	private void UpdateTerrainReplication()
	{
		if ( _gpuMesher is null || _terrainField is null ) return;
		if ( !Networking.IsHost )
		{
			if ( _terrainObservedStreamingTarget != StreamingTarget )
			{
				_terrainObservedStreamingTarget = StreamingTarget;
				_terrainLocalPlayer = null;
			}
			if ( !_terrainLocalPlayer.IsValid() || _terrainLocalPlayer.IsProxy )
			{
				_terrainUseLocalStreamingTarget = !StreamingTarget.IsValid() || StreamingTarget.Components.Get<PlayerController>().IsValid();
				_terrainLocalPlayer = Scene.GetAllComponents<PlayerController>().FirstOrDefault( player => !player.IsProxy );
			}
		}
		var presentationReady = Networking.IsHost ? !_terrainAuthorityLost :
			HasTerrainReplicaCoverage( TerrainCoverage( ActiveStreamingTarget.WorldPosition, false ) );
		_gpuMesher.SetFieldPresentationReady( presentationReady );
		if ( !Networking.IsActive || _terrainAuthorityLost ) return;
		if ( !Networking.IsHost ) { UpdateTerrainReceiver(); return; }
		if ( _terrainReplicationWorld != CurrentField.WorldId || _terrainReplicationFieldEpoch != CurrentField.Epoch )
		{
			if ( _terrainReplicationWorld != Guid.Empty ) _terrainNetworkEpoch = Guid.NewGuid();
			_terrainReplicationWorld = CurrentField.WorldId;
			_terrainReplicationFieldEpoch = CurrentField.Epoch;
			_terrainCallers.Clear();
			foreach ( var peer in _terrainPeerOrder )
			{
				CancelTerrainPeerWork( peer ); peer.Ready = false;
				peer.Known = new(); peer.ObservedRevision = -1;
			}
		}
		foreach ( var connection in Connection.All )
		{
			if ( connection == Connection.Local || !connection.IsActive || _terrainPeers.ContainsKey( connection.Id ) ||
				!_terrainSessionPlayers.TryGetValue( connection.Id, out var player ) || _terrainPeers.Count >= MaximumTerrainCallers ) continue;
			var peer = new TerrainPeer { Connection = connection, Player = player };
			_terrainPeers.Add( connection.Id, peer ); _terrainPeerOrder.Add( peer );
		}
		var now = Stopwatch.GetTimestamp();
		var elapsed = _terrainSendTimestamp == 0 ? 0 : Stopwatch.GetElapsedTime( _terrainSendTimestamp, now ).TotalSeconds;
		_terrainSendTimestamp = now;
		_terrainSendCredit = Math.Min( 8 * TerrainFragmentBytes, _terrainSendCredit + elapsed * TerrainGlobalBytesPerSecond );
		var active = _terrainPeerOrder.Count( peer => peer.Transfer is not null || peer.Preparing is not null );
		var packets = 0;
		for ( var index = 0; index < _terrainPeerOrder.Count; index++ )
		{
			var peer = _terrainPeerOrder[(_terrainSendCursor + index) % _terrainPeerOrder.Count];
			if ( !peer.Connection.IsActive || !peer.Player.IsValid() ) continue;
			var creditElapsed = peer.LastCredit == 0 ? 0 : Stopwatch.GetElapsedTime( peer.LastCredit, now ).TotalSeconds;
			peer.LastCredit = now;
			peer.Credit = Math.Min( 4 * TerrainFragmentBytes, peer.Credit + creditElapsed * TerrainPeerBytesPerSecond );
			if ( peer.Preparing?.IsCompleted == true )
			{
				try
				{
					peer.Transfer = peer.Preparing.GetAwaiter().GetResult();
					if ( peer.Transfer is not null ) { peer.Transfer.Id = ++_nextTerrainTransferId; peer.Transfer.Started = now; }
				}
				catch ( Exception exception ) { FailTerrainPeer( peer, exception.Message ); }
				peer.Preparing = null;
				if ( peer.Transfer is null ) { CancelTerrainPeerWork( peer ); active--; }
			}
			if ( peer.Transfer is null && peer.Preparing is null && active < MaximumActiveTerrainTransfers &&
				(peer.LastCheck == 0 || Stopwatch.GetElapsedTime( peer.LastCheck, now ).TotalSeconds >= 0.05) )
			{
				peer.LastCheck = now;
				var coverage = TerrainCoverage( peer.Player.WorldPosition, true );
				var source = CurrentField;
				if ( source.Revision != peer.ObservedRevision || coverage != peer.ObservedCoverage )
				{
					peer.ObservedRevision = source.Revision; peer.ObservedCoverage = coverage;
					var known = peer.Known; var ready = peer.Ready; var previous = peer.Coverage;
					peer.WorkCancellation = CancellationTokenSource.CreateLinkedTokenSource( _terrainEditCancellation.Token );
					var cancellation = peer.WorkCancellation.Token;
					peer.Preparing = Task.RunInThreadAsync( () => PrepareTerrainTransfer( source, coverage, known, ready, previous, cancellation ) );
					active++;
				}
			}
			var transfer = peer.Transfer;
			if ( transfer is not null )
			{
				if ( Stopwatch.GetElapsedTime( transfer.Started, now ).TotalSeconds > TerrainTransferTimeoutSeconds )
					FailTerrainPeer( peer, "Terrain transfer timed out." );
				else
				{
					while ( packets < 8 && Stopwatch.GetElapsedTime( now ).TotalMilliseconds < 0.5 && SendTerrainFragment( peer ) ) packets++;
				}
			}
			if ( Stopwatch.GetElapsedTime( now ).TotalMilliseconds >= 0.5 ) break;
		}
		if ( _terrainPeerOrder.Count > 0 ) _terrainSendCursor = (_terrainSendCursor + 1) % _terrainPeerOrder.Count;
	}

	private bool SendTerrainFragment( TerrainPeer peer )
	{
		var transfer = peer.Transfer;
		if ( transfer.EndSent ) return false;
		if ( transfer.Buffer is null )
		{
			if ( transfer.Block == transfer.Manifest.Changed.Count )
			{
				using ( Rpc.FilterInclude( peer.Connection ) ) FinishTerrainTransfer( _terrainNetworkEpoch, transfer.Id );
				transfer.EndSent = true;
				return true;
			}
			if ( transfer.SentPages - transfer.AcknowledgedPages >= TerrainPageWindow ) return false;
			if ( transfer.Encoding is null )
			{
				var key = transfer.Manifest.Changed[transfer.Block]; var page = transfer.Pages[transfer.Block];
				var cancellation = peer.WorkCancellation.Token;
				transfer.Encoding = Task.RunInThreadAsync( () =>
				{
					cancellation.ThrowIfCancellationRequested();
					var encoded = TerrainFieldStore.EncodePageVersion( key, page );
					cancellation.ThrowIfCancellationRequested();
					return encoded;
				} );
				return false;
			}
			if ( !transfer.Encoding.IsCompleted ) return false;
			try { transfer.Buffer = transfer.Encoding.GetAwaiter().GetResult(); }
			catch ( Exception exception ) { FailTerrainPeer( peer, exception.Message ); return false; }
			transfer.Encoding = null;
		}
		var count = Math.Min( TerrainFragmentBytes, transfer.Buffer.Length - transfer.Offset );
		if ( _terrainSendCredit < count || peer.Credit < count ) return false;
		var fragment = new byte[count];
		Array.Copy( transfer.Buffer, transfer.Offset, fragment, 0, count );
		using ( Rpc.FilterInclude( peer.Connection ) )
			ReceiveTerrainFragment( _terrainNetworkEpoch, transfer.Id, transfer.Block, transfer.Buffer.Length, transfer.Offset, fragment );
		_terrainSendCredit -= count; peer.Credit -= count; _terrainBytesSent += count;
		transfer.Offset += count;
		if ( transfer.Offset == transfer.Buffer.Length )
		{
			if ( transfer.Block >= 0 ) transfer.SentPages++;
			transfer.Block++; transfer.Offset = 0; transfer.Buffer = null;
		}
		return true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void ReceiveTerrainFragment( Guid epoch, long id, int block, int totalBytes, int offset, byte[] bytes )
	{
		if ( Networking.IsHost || epoch == Guid.Empty || id < 1 || _terrainRetiredEpochs.Contains( epoch ) ) return;
		if ( epoch == _terrainReceivingEpoch && id <= _terrainLastAppliedTransfer ) return;
		try
		{
			if ( block == -1 && offset == 0 && (_terrainIncoming is null || epoch != _terrainIncoming.Epoch || id > _terrainIncoming.Id) )
			{
				if ( _terrainIncoming?.Applying == true ) _activeTerrainEditCancellation?.Cancel();
				_terrainIncoming?.WorkCancellation.Cancel();
				_terrainIncoming?.WorkCancellation.Dispose();
				if ( epoch != _terrainReceivingEpoch )
				{
					if ( _terrainReceivingEpoch != Guid.Empty ) _terrainRetiredEpochs.Add( _terrainReceivingEpoch );
					if ( _terrainRetiredEpochs.Count > 64 ) throw new InvalidDataException( "Terrain session epoch budget exhausted; reconnect." );
					_terrainReceivingEpoch = epoch; _terrainReplicaEpoch = Guid.Empty;
					_terrainReplicaVersions = new(); _terrainLastAppliedTransfer = 0;
				}
				_terrainIncoming = new TerrainIncoming { Epoch = epoch, Id = id, Started = Stopwatch.GetTimestamp(),
					WorkCancellation = CancellationTokenSource.CreateLinkedTokenSource( _terrainEditCancellation.Token ) };
			}
			var incoming = _terrainIncoming;
			if ( incoming is null || incoming.Id != id || incoming.Epoch != epoch ) return;
			if ( bytes is null || bytes.Length < 1 || bytes.Length > TerrainFragmentBytes || totalBytes < 1 ||
				totalBytes > (block == -1 ? TerrainReplicationManifest.MaximumBytes : TerrainFieldCodec.MaximumPageBlockBytes) ||
				offset < 0 || (long)offset + bytes.Length > totalBytes || block < -1 )
				throw new InvalidDataException( "Terrain fragment bounds are invalid." );
			if ( block == -1 && incoming.Manifest is not null || block >= 0 && block < incoming.ReceivedPages ) return;
			if ( block >= 0 && (incoming.Manifest is null || block != incoming.ReceivedPages || block >= incoming.Manifest.Changed.Count) )
				throw new InvalidDataException( "Terrain page block is out of order." );
			if ( incoming.Block is null )
			{
				if ( offset != 0 ) throw new InvalidDataException( "Terrain block starts at an invalid offset." );
				incoming.Block = new byte[totalBytes]; incoming.Offset = 0;
			}
			if ( offset < incoming.Offset ) return;
			if ( incoming.Block.Length != totalBytes || offset != incoming.Offset ) throw new InvalidDataException( "Terrain fragment is out of order." );
			Array.Copy( bytes, 0, incoming.Block, offset, bytes.Length );
			incoming.Offset += bytes.Length; _terrainBytesReceived += bytes.Length;
			if ( incoming.Offset != totalBytes ) return;
			if ( block == -1 )
			{
				incoming.Manifest = TerrainReplicationManifest.Decode( incoming.Block, CurrentField.Settings );
				var changed = new HashSet<Vector3Int>( incoming.Manifest.Changed );
				foreach ( var pair in incoming.Manifest.Versions )
				{
					if ( changed.Contains( pair.Key ) ) continue;
					if ( !_terrainReplicaVersions.TryGetValue( pair.Key, out var revision ) || revision != pair.Value ||
						!CurrentField.Pages.TryGetValue( pair.Key, out var page ) ) throw new InvalidDataException( "Terrain cached page does not match manifest." );
					incoming.Pages.Add( pair.Key, page );
				}
			}
			else
			{
				if ( incoming.ReceivedPages - incoming.DecodedPages >= TerrainPageWindow ) throw new InvalidDataException( "Terrain receive window overflow." );
				incoming.Encoded.Enqueue( incoming.Block ); incoming.ReceivedPages++;
			}
			incoming.Block = null; incoming.Offset = 0;
		}
		catch ( Exception exception ) { FailTerrainReceive( exception.Message ); }
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void FinishTerrainTransfer( Guid epoch, long id )
	{
		if ( Networking.IsHost || _terrainIncoming is null || _terrainIncoming.Epoch != epoch || _terrainIncoming.Id != id ) return;
		_terrainIncoming.EndReceived = true;
	}

	private void UpdateTerrainReceiver()
	{
		var incoming = _terrainIncoming;
		if ( incoming is null ) return;
		try
		{
			if ( Stopwatch.GetElapsedTime( incoming.Started ).TotalSeconds > TerrainTransferTimeoutSeconds ) throw new InvalidDataException( "Terrain receive timed out." );
			if ( incoming.Decoding?.IsCompleted == true )
			{
				var decoded = incoming.Decoding.GetAwaiter().GetResult();
				if ( decoded.Coordinate != incoming.Manifest.Changed[incoming.DecodedPages] ||
					decoded.Page.Revision != incoming.Manifest.Versions[decoded.Coordinate] || !incoming.Pages.TryAdd( decoded.Coordinate, decoded.Page ) )
					throw new InvalidDataException( "Terrain page does not match transfer manifest." );
				AcknowledgeTerrainTransfer( incoming.Epoch, incoming.Id, incoming.DecodedPages, false, true );
				incoming.DecodedPages++; incoming.Decoding = null;
			}
			if ( incoming.Decoding is null && incoming.Encoded.TryDequeue( out var block ) )
			{
				var revision = incoming.Manifest.Revision;
				var cancellation = incoming.WorkCancellation.Token;
				incoming.Decoding = Task.RunInThreadAsync( () =>
				{
					cancellation.ThrowIfCancellationRequested();
					var decoded = TerrainFieldCodec.DecodePageBlock( block, revision );
					cancellation.ThrowIfCancellationRequested();
					return decoded;
				} );
			}
			if ( !incoming.EndReceived || incoming.Manifest is null || incoming.DecodedPages != incoming.Manifest.Changed.Count ) return;
			if ( !incoming.Applying )
			{
				if ( _terrainEditTask is not null || _gpuMesher.FieldPublicationPending ) return;
				if ( incoming.Pages.Count != incoming.Manifest.Versions.Count ) throw new InvalidDataException( "Terrain manifest is incomplete." );
				var source = CurrentField;
				var target = new TerrainFieldSnapshot( source.Settings, incoming.Manifest.Revision, incoming.Pages, incoming.Manifest.WorldId );
				_activeTerrainEditCancellation = CancellationTokenSource.CreateLinkedTokenSource( _terrainEditCancellation.Token );
				var cancellation = _activeTerrainEditCancellation.Token;
				incoming.RequestId = ++_nextTerrainEditId;
				_activeTerrainEdit = new TerrainEditIntent( incoming.RequestId, default, 0, 0, Stopwatch.GetTimestamp() );
				incoming.Applying = true;
				var resetEpoch = _terrainReplicaEpoch != incoming.Epoch;
				_terrainEditTask = Task.RunInThreadAsync( () => TerrainField.PrepareReplacement( source, target, cancellation, resetEpoch ) );
			}
			if ( !incoming.Committed || _gpuMesher.FieldPublicationPending ) return;
			var player = _terrainLocalPlayer;
			if ( !player.IsValid() ) return;
			var bounds = player.BodyBox();
			if ( !_collision.IsReady( WorldToChunkCoordinate( player.WorldPosition + bounds.Mins ),
				WorldToChunkCoordinate( player.WorldPosition + bounds.Maxs ) ) ) return;
			_terrainReplicaVersions = incoming.Manifest.Versions;
			_terrainReplicaCoverage = incoming.Manifest.Coverage;
			_terrainReplicaEpoch = incoming.Epoch;
			_terrainLastAppliedTransfer = incoming.Id;
			_terrainTransfersApplied++;
			_terrainNetworkFailure = null;
			AcknowledgeTerrainTransfer( incoming.Epoch, incoming.Id, -1, true, true );
			Log.Info( $"[TerrainNetwork] applied transfer={incoming.Id} hostRevision={incoming.Manifest.Revision} localRevision={CurrentField.Revision} pages={incoming.Pages.Count} bytes={_terrainBytesReceived}" );
			incoming.WorkCancellation.Dispose();
			_terrainIncoming = null;
		}
		catch ( Exception exception ) { FailTerrainReceive( exception.Message ); }
	}

	[Rpc.Host]
	private void AcknowledgeTerrainTransfer( Guid epoch, long id, int page, bool complete, bool accepted )
	{
		var caller = Rpc.Caller;
		if ( !Networking.IsHost || caller is null || epoch != _terrainNetworkEpoch || !_terrainPeers.TryGetValue( caller.Id, out var peer ) ||
			peer.Transfer is null || peer.Transfer.Id != id ) return;
		var transfer = peer.Transfer;
		if ( !accepted ) { FailTerrainPeer( peer, "Client rejected terrain transfer." ); return; }
		if ( complete )
		{
			if ( !transfer.EndSent || transfer.AcknowledgedPages != transfer.Manifest.Changed.Count ) return;
			peer.Known = transfer.Manifest.Versions; peer.Coverage = transfer.Manifest.Coverage;
			peer.Ready = true; peer.Failures = 0; CancelTerrainPeerWork( peer );
			Log.Info( $"[TerrainNetwork] acknowledged connection={caller.Id} transfer={id} revision={transfer.Manifest.Revision} pages={peer.Known.Count}" );
		}
		else if ( page == transfer.AcknowledgedPages && page < transfer.SentPages ) transfer.AcknowledgedPages++;
	}

	private void FailTerrainReceive( string reason )
	{
		var incoming = _terrainIncoming;
		if ( incoming?.Applying == true ) _activeTerrainEditCancellation?.Cancel();
		incoming?.WorkCancellation.Cancel();
		incoming?.WorkCancellation.Dispose();
		_terrainIncoming = null; _terrainReplicaEpoch = Guid.Empty;
		_terrainNetworkFailure = reason;
		if ( incoming is not null ) AcknowledgeTerrainTransfer( incoming.Epoch, incoming.Id, -1, true, false );
		if ( incoming?.FieldCommitted == true )
		{
			_terrainAuthorityLost = true;
			Networking.Disconnect();
		}
		Log.Error( $"[TerrainNetwork] receive.failed reason={reason}" );
	}

	private static void CancelTerrainPeerWork( TerrainPeer peer )
	{
		peer.WorkCancellation?.Cancel();
		peer.WorkCancellation?.Dispose();
		peer.WorkCancellation = null;
		peer.Transfer = null;
		peer.Preparing = null;
	}

	private void FailTerrainPeer( TerrainPeer peer, string reason )
	{
		CancelTerrainPeerWork( peer ); peer.Known = new(); peer.Ready = false;
		peer.ObservedRevision = -1; peer.LastCheck = Stopwatch.GetTimestamp(); peer.Failures++;
		_terrainNetworkFailure = reason;
		Log.Error( $"[TerrainNetwork] send.failed connection={peer.Connection.Id} reason={reason}" );
		if ( peer.Failures >= 3 ) peer.Connection.Kick( "Terrain synchronization failed. Please reconnect." );
	}

	public void OnBecameHost( Connection previousHost )
	{
		_terrainAuthorityLost = true;
		Networking.Disconnect();
		Log.Error( "[TerrainNetwork] host_migration.unsupported reason=Replica is not a complete authoritative world" );
	}
}
