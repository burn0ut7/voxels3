using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>Regional host page identities; density payloads use TerrainFieldCodec unchanged.</summary>
internal sealed class TerrainReplicationManifest
{
	public const int ProtocolVersion = 2;
	public const int MaximumBytes = 65536;
	public Guid WorldId;
	public int Revision;
	public ProceduralTerrainSettings Settings;
	public SdfWorldAabb Coverage;
	public readonly Dictionary<Vector3Int, int> Versions = new();
	public readonly List<Vector3Int> Changed = new();

	public static bool Contains( SdfWorldAabb outer, SdfWorldAabb inner ) =>
		outer.Minimum.x <= inner.Minimum.x && outer.Minimum.y <= inner.Minimum.y && outer.Minimum.z <= inner.Minimum.z &&
		outer.Maximum.x >= inner.Maximum.x && outer.Maximum.y >= inner.Maximum.y && outer.Maximum.z >= inner.Maximum.z;

	public byte[] Encode()
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream, Encoding.UTF8, true );
		writer.Write( ProtocolVersion );
		using var baseline = new MemoryStream();
		TerrainFieldCodec.WriteIdentity( baseline, new TerrainFieldIdentity( Settings, Revision, WorldId ) );
		var header = baseline.ToArray();
		writer.Write( header.Length ); writer.Write( header );
		writer.Write( Coverage.Minimum.x ); writer.Write( Coverage.Minimum.y ); writer.Write( Coverage.Minimum.z );
		writer.Write( Coverage.Maximum.x ); writer.Write( Coverage.Maximum.y ); writer.Write( Coverage.Maximum.z );
		writer.Write( Versions.Count );
		var changed = new HashSet<Vector3Int>( Changed );
		foreach ( var pair in Versions )
		{
			writer.Write( pair.Key.x ); writer.Write( pair.Key.y ); writer.Write( pair.Key.z );
			writer.Write( pair.Value ); writer.Write( changed.Contains( pair.Key ) );
		}
		writer.Flush();
		writer.Write( SHA256.HashData( stream.ToArray() ) );
		return stream.ToArray();
	}

	public static TerrainReplicationManifest Decode( byte[] bytes, ProceduralTerrainSettings expected )
	{
		if ( bytes is null || bytes.Length < 32 || bytes.Length > MaximumBytes ) throw new InvalidDataException( "Terrain manifest exceeds its byte budget." );
		if ( !SHA256.HashData( bytes.AsSpan( 0, bytes.Length - 32 ) ).AsSpan().SequenceEqual( bytes.AsSpan( bytes.Length - 32 ) ) )
			throw new InvalidDataException( "Terrain manifest checksum does not match." );
		using var stream = new MemoryStream( bytes, 0, bytes.Length - 32, false );
		using var reader = new BinaryReader( stream );
		if ( reader.ReadInt32() != ProtocolVersion ) throw new InvalidDataException( "Terrain transfer protocol does not match." );
		var headerLength = reader.ReadInt32();
		if ( headerLength != TerrainFieldCodec.IdentityBytes ) throw new InvalidDataException( "Terrain manifest baseline length is invalid." );
		using var baseline = new MemoryStream( reader.ReadBytes( headerLength ), false );
		var header = TerrainFieldCodec.ReadIdentity( baseline, expected );
		var low = new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
		var high = new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
		if ( !float.IsFinite( low.x ) || !float.IsFinite( low.y ) || !float.IsFinite( low.z ) ||
			!float.IsFinite( high.x ) || !float.IsFinite( high.y ) || !float.IsFinite( high.z ) ||
			low.x >= high.x || low.y >= high.y || low.z >= high.z ||
			low.x < -TerrainField.MaximumWorldCoordinate || low.y < -TerrainField.MaximumWorldCoordinate || low.z < -TerrainField.MaximumWorldCoordinate ||
			high.x > TerrainField.MaximumWorldCoordinate || high.y > TerrainField.MaximumWorldCoordinate || high.z > TerrainField.MaximumWorldCoordinate )
			throw new InvalidDataException( "Terrain manifest coverage is invalid." );
		var result = new TerrainReplicationManifest { WorldId = header.WorldId, Revision = header.Revision,
			Settings = header.Settings, Coverage = new SdfWorldAabb( low, high ) };
		var count = reader.ReadInt32();
		if ( count < 0 || count > TerrainField.MaximumPages || stream.Length - stream.Position != count * 17L )
			throw new InvalidDataException( "Terrain manifest page count is invalid." );
		var pageSize = TerrainField.SampleSpacing * TerrainField.SamplesPerPageAxis;
		var coordinateLimit = (int)(TerrainField.MaximumWorldCoordinate / pageSize);
		for ( var i = 0; i < count; i++ )
		{
			var key = new Vector3Int( reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32() );
			var revision = reader.ReadInt32();
			var changed = reader.ReadByte();
			if ( revision < 1 || revision > header.Revision || changed > 1 ||
				key.x < -coordinateLimit || key.y < -coordinateLimit || key.z < -coordinateLimit ||
				key.x >= coordinateLimit || key.y >= coordinateLimit || key.z >= coordinateLimit ||
				!result.Versions.TryAdd( key, revision ) ) throw new InvalidDataException( "Terrain manifest page identity is invalid." );
			var origin = new Vector3( key.x, key.y, key.z ) * pageSize;
			if ( !TerrainFieldChange.Intersects( result.Coverage, new SdfWorldAabb( origin, origin + Vector3.One * pageSize ) ) )
				throw new InvalidDataException( "Terrain manifest page lies outside coverage." );
			if ( changed != 0 ) result.Changed.Add( key );
		}
		return result;
	}
}
