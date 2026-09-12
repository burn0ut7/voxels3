using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

// Local checkpoint epoch is not part of the stored or network identity.
internal readonly record struct TerrainFieldIdentity( ProceduralTerrainSettings Settings, int Revision, Guid WorldId, int Epoch = 0 );

/// <summary>Shared world-identity and lossless page formats for storage and host state transfer.</summary>
internal static class TerrainFieldCodec
{
	private const int Magic = 0x33465856;
	private const int HeaderBytes = 88;
	public const int IdentityBytes = HeaderBytes + 36;
	public const int MaximumPagePayloadBytes = 17 + TerrainField.SamplesPerPage * (sizeof( float ) + sizeof( ushort ));
	public const int MaximumPageBlockBytes = MaximumPagePayloadBytes + 36;

	public static byte[] EncodePage( Vector3Int coordinate, TerrainFieldPage page )
	{
		var nonzero = 0;
		for ( var i = 0; i < TerrainField.SamplesPerPage; i++ ) if ( page.Sample( i ) != 0f ) nonzero++;
		var sparse = 4 + nonzero * 6 < TerrainField.SamplesPerPage * sizeof( float );
		using var stream = new MemoryStream( sparse ? 21 + nonzero * 6 : MaximumPagePayloadBytes );
		using var writer = new BinaryWriter( stream, Encoding.UTF8, true );
		writer.Write( coordinate.x ); writer.Write( coordinate.y ); writer.Write( coordinate.z );
		writer.Write( page.Revision );
		// Modes 0/1 remain readable legacy pages; 2/3 append authoritative materials.
		writer.Write( (byte)((sparse ? 1 : 0) + (page.HasMaterials ? 2 : 0)) );
		if ( sparse ) writer.Write( nonzero );
		for ( var i = 0; i < TerrainField.SamplesPerPage; i++ )
		{
			var value = page.Sample( i );
			if ( sparse )
			{
				if ( value == 0f ) continue;
				writer.Write( (ushort)i );
			}
			writer.Write( value );
		}
		if ( page.HasMaterials )
			for ( var i = 0; i < TerrainField.SamplesPerPage; i++ ) writer.Write( page.Material( i ) );
		return stream.ToArray();
	}

	/// <summary>Content identity for explicit regional diagnostics, independent of local revision numbering.</summary>
	public static string RegionFingerprint( TerrainFieldSnapshot snapshot, SdfWorldAabb bounds, CancellationToken cancellation )
	{
		using var stream = new MemoryStream();
		WriteIdentity( stream, new TerrainFieldIdentity( snapshot.Settings, 0, snapshot.WorldId ) );
		using var writer = new BinaryWriter( stream, Encoding.UTF8, true );
		writer.Write( bounds.Minimum.x ); writer.Write( bounds.Minimum.y ); writer.Write( bounds.Minimum.z );
		writer.Write( bounds.Maximum.x ); writer.Write( bounds.Maximum.y ); writer.Write( bounds.Maximum.z );
		var size = TerrainField.SampleSpacing * TerrainField.SamplesPerPageAxis;
		foreach ( var pair in snapshot.Pages.OrderBy( pair => pair.Key.z ).ThenBy( pair => pair.Key.y ).ThenBy( pair => pair.Key.x ) )
		{
			cancellation.ThrowIfCancellationRequested();
			var origin = new Vector3( pair.Key.x, pair.Key.y, pair.Key.z ) * size;
			if ( !TerrainFieldChange.Intersects( bounds, new SdfWorldAabb( origin, origin + Vector3.One * size ) ) ) continue;
			var page = pair.Value;
			if ( page.Minimum == 0f && page.Maximum == 0f && !page.HasMaterials ) continue;
			var payload = EncodePage( pair.Key, TerrainFieldStore.PinForRead( page ) );
			// Page coordinates and exact codec sample values remain; revision is local bookkeeping.
			Array.Clear( payload, 12, sizeof( int ) );
			writer.Write( SHA256.HashData( payload ) );
		}
		cancellation.ThrowIfCancellationRequested();
		return Convert.ToHexString( SHA256.HashData( stream.ToArray() ) );
	}

	public static byte[] EncodePageBlock( Vector3Int coordinate, TerrainFieldPage page )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream, Encoding.UTF8, true );
		WriteBlock( writer, EncodePage( coordinate, page ) );
		return stream.ToArray();
	}

	public static (Vector3Int Coordinate, TerrainFieldPage Page) DecodePageBlock( byte[] block, int maximumRevision, TerrainFieldPage.SampleReservation reservation = null, float[] metadataScratch = null )
	{
		using var stream = new MemoryStream( block, false );
		using var reader = new BinaryReader( stream );
		var result = DecodePage( ReadBlock( reader, MaximumPagePayloadBytes ), maximumRevision, reservation, metadataScratch );
		if ( stream.Position != stream.Length ) throw new InvalidDataException( "Terrain page block contains trailing data." );
		return result;
	}

	public static (Vector3Int Coordinate, TerrainFieldPage Page) DecodePage( byte[] payload, int maximumRevision, TerrainFieldPage.SampleReservation reservation = null, float[] metadataScratch = null )
	{
		if ( payload is null || payload.Length < 17 || payload.Length > MaximumPagePayloadBytes )
			throw new InvalidDataException( "Terrain page length is invalid." );
		using var stream = new MemoryStream( payload, false );
		using var reader = new BinaryReader( stream );
		var coordinate = new Vector3Int( reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32() );
		var limit = (int)(TerrainField.MaximumWorldCoordinate / (TerrainField.SampleSpacing * TerrainField.SamplesPerPageAxis));
		if ( coordinate.x < -limit || coordinate.x >= limit || coordinate.y < -limit || coordinate.y >= limit ||
			coordinate.z < -limit || coordinate.z >= limit ) throw new InvalidDataException( "Terrain page coordinate is out of bounds." );
		var revision = reader.ReadInt32();
		if ( revision < 1 || revision > maximumRevision ) throw new InvalidDataException( "Terrain page revision is invalid." );
		var mode = reader.ReadByte();
		if ( mode > 3 ) throw new InvalidDataException( "Terrain page encoding is unsupported." );
		var hasMaterials = mode >= 2;
		var sparse = (mode & 1) != 0;
		var count = sparse ? reader.ReadInt32() : TerrainField.SamplesPerPage;
		if ( count < 0 || count > TerrainField.SamplesPerPage ||
			stream.Length - stream.Position != (long)count * (sparse ? 6 : 4) + (hasMaterials ? TerrainField.SamplesPerPage * sizeof( ushort ) : 0) )
			throw new InvalidDataException( "Terrain sample count is invalid." );
		if ( metadataScratch is not null && (reservation is not null || metadataScratch.Length != TerrainField.SamplesPerPage) )
			throw new ArgumentException( "Invalid terrain metadata scratch buffer." );
		var values = metadataScratch ?? TerrainFieldPage.AllocateValues( reservation );
		// Sparse payloads omit zero samples. A reused validation buffer must not
		// carry those samples over from the previously validated page.
		if ( metadataScratch is not null && sparse ) Array.Clear( values );
		var previousIndex = -1;
		for ( var i = 0; i < count; i++ )
		{
			var index = sparse ? reader.ReadUInt16() : i;
			if ( index <= previousIndex || index >= values.Length ) throw new InvalidDataException( "Terrain sample indices are invalid." );
			previousIndex = index;
			values[index] = reader.ReadSingle();
		}
		// The canonical page constructor validates finite, bounded corrections.
		ushort[] materials = null;
		if ( hasMaterials )
		{
			materials = new ushort[TerrainField.SamplesPerPage];
			for ( var i = 0; i < materials.Length; i++ ) materials[i] = reader.ReadUInt16();
		}
		return (coordinate, new TerrainFieldPage( revision, values, retainSamples: metadataScratch is null, materials: materials ));
	}

	public static void WriteIdentity( Stream stream, TerrainFieldIdentity identity )
	{
		using var writer = new BinaryWriter( stream, Encoding.UTF8, true );
		using var header = new MemoryStream( HeaderBytes );
		using ( var fields = new BinaryWriter( header, Encoding.UTF8, true ) )
		{
			fields.Write( Magic ); fields.Write( TerrainField.FormatVersion );
			fields.Write( ProceduralTerrainSdf.CurrentVersion ); fields.Write( TerrainField.SampleSpacing );
			fields.Write( TerrainField.SamplesPerPageAxis ); fields.Write( identity.Settings.WorldSeed );
			fields.Write( identity.Settings.LandAmount );
			fields.Write( identity.Settings.MountainAmount );
			fields.Write( identity.Settings.PlainsAmount );
			fields.Write( identity.Settings.ContinentalScale );
			fields.Write( identity.Settings.MountainRegionScale );
			fields.Write( identity.Settings.LocalLandformScale );
			fields.Write( identity.Settings.ReliefHeight );
			fields.Write( identity.Settings.Ruggedness );
			fields.Write( SurfaceWater.CurrentVersion ); fields.Write( identity.Settings.SeaLevel );
			fields.Write( identity.Revision ); fields.Write( 0 );
			fields.Write( identity.WorldId.ToByteArray() );
		}
		WriteBlock( writer, header.ToArray() );
		writer.Flush();
	}

	public static TerrainFieldIdentity ReadIdentity( Stream stream, ProceduralTerrainSettings expected )
	{
		if ( stream.CanSeek && stream.Length > IdentityBytes ) throw new InvalidDataException( "Terrain identity exceeds its byte budget." );
		using var reader = new BinaryReader( stream, Encoding.UTF8, true );
		using var header = new MemoryStream( ReadBlock( reader, HeaderBytes ), false );
		if ( header.Length != HeaderBytes ) throw new InvalidDataException( "Terrain header length is invalid." );
		using var fields = new BinaryReader( header );
		if ( fields.ReadInt32() != Magic || fields.ReadInt32() != TerrainField.FormatVersion ||
			fields.ReadInt32() != ProceduralTerrainSdf.CurrentVersion || fields.ReadSingle() != TerrainField.SampleSpacing ||
			fields.ReadInt32() != TerrainField.SamplesPerPageAxis ) throw new InvalidDataException( "Terrain format or generator version does not match." );
		var settings = new ProceduralTerrainSettings( fields.ReadInt32(), fields.ReadSingle(), fields.ReadSingle(), fields.ReadSingle(), fields.ReadSingle(), fields.ReadSingle(), fields.ReadSingle(), fields.ReadSingle(), fields.ReadSingle() );
		if ( fields.ReadInt32() != SurfaceWater.CurrentVersion )
			throw new InvalidDataException( "Water generator version does not match." );
		settings = settings with { SeaLevel = fields.ReadSingle() };
		if ( !settings.IsValid || settings != expected ) throw new InvalidDataException( "Terrain generator settings do not match this world." );
		var revision = fields.ReadInt32();
		var count = fields.ReadInt32();
		var worldId = new Guid( fields.ReadBytes( 16 ) );
		if ( worldId == Guid.Empty ) throw new InvalidDataException( "Terrain world identity is empty." );
		if ( revision < 0 || count != 0 )
			throw new InvalidDataException( "Terrain identity revision or page count is invalid." );
		if ( stream.ReadByte() != -1 ) throw new InvalidDataException( "Terrain identity contains trailing data." );
		return new TerrainFieldIdentity( settings, revision, worldId );
	}

	private static void WriteBlock( BinaryWriter writer, byte[] payload )
	{
		writer.Write( payload.Length );
		writer.Write( payload );
		writer.Write( SHA256.HashData( payload ) );
	}

	private static byte[] ReadBlock( BinaryReader reader, int maximumLength )
	{
		var length = reader.ReadInt32();
		if ( length < 0 || length > maximumLength ) throw new InvalidDataException( "Terrain block exceeds its byte budget." );
		var payload = reader.ReadBytes( length );
		var checksum = reader.ReadBytes( 32 );
		if ( payload.Length != length || checksum.Length != 32 || !SHA256.HashData( payload ).AsSpan().SequenceEqual( checksum ) )
			throw new InvalidDataException( "Terrain block is truncated or corrupt." );
		return payload;
	}
}
