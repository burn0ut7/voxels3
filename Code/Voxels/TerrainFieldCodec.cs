using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

/// <summary>One lossless absolute-page format for saved worlds and host state transfer.</summary>
internal static class TerrainFieldCodec
{
	private const int Magic = 0x33465856;
	private const int HeaderBytes = 60;
	public const int EmptySnapshotBytes = HeaderBytes + 36;
	public const int MaximumPagePayloadBytes = 17 + TerrainField.SamplesPerPage * sizeof( float );
	public const int MaximumPageBlockBytes = MaximumPagePayloadBytes + 36;
	public const long MaximumSnapshotBytes = HeaderBytes + 36L + (long)TerrainField.MaximumPages * MaximumPageBlockBytes;

	public static byte[] EncodePage( Vector3Int coordinate, TerrainFieldPage page )
	{
		var nonzero = 0;
		for ( var i = 0; i < TerrainField.SamplesPerPage; i++ ) if ( page.Sample( i ) != 0f ) nonzero++;
		var sparse = 4 + nonzero * 6 < TerrainField.SamplesPerPage * sizeof( float );
		using var stream = new MemoryStream( sparse ? 21 + nonzero * 6 : MaximumPagePayloadBytes );
		using var writer = new BinaryWriter( stream, Encoding.UTF8, true );
		writer.Write( coordinate.x ); writer.Write( coordinate.y ); writer.Write( coordinate.z );
		writer.Write( page.Revision );
		writer.Write( (byte)(sparse ? 1 : 0) );
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
		return stream.ToArray();
	}

	/// <summary>Content identity for explicit regional diagnostics, independent of local revision numbering.</summary>
	public static string RegionFingerprint( TerrainFieldSnapshot snapshot, SdfWorldAabb bounds, CancellationToken cancellation )
	{
		using var stream = new MemoryStream();
		WriteSnapshot( stream, new TerrainFieldSnapshot( snapshot.Settings, 0,
			new Dictionary<Vector3Int, TerrainFieldPage>(), snapshot.WorldId ), cancellation );
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
			if ( page.Minimum == 0f && page.Maximum == 0f ) continue;
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
		if ( mode > 1 ) throw new InvalidDataException( "Terrain page encoding is unsupported." );
		var count = mode == 0 ? TerrainField.SamplesPerPage : reader.ReadInt32();
		if ( count < 0 || count > TerrainField.SamplesPerPage ||
			stream.Length - stream.Position != (long)count * (mode == 0 ? 4 : 6) )
			throw new InvalidDataException( "Terrain sample count is invalid." );
		if ( metadataScratch is not null && (reservation is not null || metadataScratch.Length != TerrainField.SamplesPerPage) )
			throw new ArgumentException( "Invalid terrain metadata scratch buffer." );
		var values = metadataScratch ?? TerrainFieldPage.AllocateValues( reservation );
		// Sparse payloads omit zero samples. A reused validation buffer must not
		// carry those samples over from the previously validated page.
		if ( metadataScratch is not null && mode == 1 ) Array.Clear( values );
		var previousIndex = -1;
		for ( var i = 0; i < count; i++ )
		{
			var index = mode == 0 ? i : reader.ReadUInt16();
			if ( index <= previousIndex || index >= values.Length ) throw new InvalidDataException( "Terrain sample indices are invalid." );
			previousIndex = index;
			values[index] = reader.ReadSingle();
		}
		// The canonical page constructor validates finite, bounded corrections.
		return (coordinate, new TerrainFieldPage( revision, values, retainSamples: metadataScratch is null ));
	}

	public static void WriteSnapshot( Stream stream, TerrainFieldSnapshot snapshot, CancellationToken cancellation )
	{
		using var writer = new BinaryWriter( stream, Encoding.UTF8, true );
		using var header = new MemoryStream( HeaderBytes );
		using ( var fields = new BinaryWriter( header, Encoding.UTF8, true ) )
		{
			fields.Write( Magic ); fields.Write( TerrainField.FormatVersion );
			fields.Write( ProceduralTerrainSdf.CurrentVersion ); fields.Write( TerrainField.SampleSpacing );
			fields.Write( TerrainField.SamplesPerPageAxis ); fields.Write( snapshot.Settings.WorldSeed );
			fields.Write( snapshot.Settings.SurfaceBaseHeight ); fields.Write( snapshot.Settings.SurfaceFrequency );
			fields.Write( snapshot.Settings.SurfaceAmplitude ); fields.Write( snapshot.Revision ); fields.Write( snapshot.PageCount );
			fields.Write( snapshot.WorldId.ToByteArray() );
		}
		WriteBlock( writer, header.ToArray() );
		var keys = snapshot.Pages.Keys.ToArray();
		Array.Sort( keys, ( a, b ) =>
		{
			var order = a.z.CompareTo( b.z );
			if ( order != 0 ) return order;
			order = a.y.CompareTo( b.y );
			return order != 0 ? order : a.x.CompareTo( b.x );
		} );
		foreach ( var key in keys )
		{
			cancellation.ThrowIfCancellationRequested();
			WriteBlock( writer, EncodePage( key, snapshot.Pages[key] ) );
		}
		writer.Flush();
	}

	public static TerrainFieldSnapshot ReadSnapshot( Stream stream, ProceduralTerrainSettings expected, CancellationToken cancellation )
	{
		if ( stream.CanSeek && stream.Length > MaximumSnapshotBytes ) throw new InvalidDataException( "Terrain snapshot exceeds its byte budget." );
		using var reader = new BinaryReader( stream, Encoding.UTF8, true );
		using var header = new MemoryStream( ReadBlock( reader, HeaderBytes ), false );
		if ( header.Length != HeaderBytes ) throw new InvalidDataException( "Terrain header length is invalid." );
		using var fields = new BinaryReader( header );
		if ( fields.ReadInt32() != Magic || fields.ReadInt32() != TerrainField.FormatVersion ||
			fields.ReadInt32() != ProceduralTerrainSdf.CurrentVersion || fields.ReadSingle() != TerrainField.SampleSpacing ||
			fields.ReadInt32() != TerrainField.SamplesPerPageAxis ) throw new InvalidDataException( "Terrain format or generator version does not match." );
		var settings = new ProceduralTerrainSettings( fields.ReadInt32(), fields.ReadSingle(), fields.ReadSingle(), fields.ReadSingle() );
		if ( settings != expected ) throw new InvalidDataException( "Terrain generator settings do not match this world." );
		var revision = fields.ReadInt32();
		var count = fields.ReadInt32();
		var worldId = new Guid( fields.ReadBytes( 16 ) );
		if ( worldId == Guid.Empty ) throw new InvalidDataException( "Terrain world identity is empty." );
		if ( revision < 0 || count < 0 || count > TerrainField.MaximumPages || (count > 0 && revision == 0) )
			throw new InvalidDataException( "Terrain snapshot revision or page count is invalid." );
		var pages = new Dictionary<Vector3Int, TerrainFieldPage>( count );
		for ( var i = 0; i < count; i++ )
		{
			cancellation.ThrowIfCancellationRequested();
			var decoded = DecodePage( ReadBlock( reader, MaximumPagePayloadBytes ), revision );
			if ( !pages.TryAdd( decoded.Coordinate, decoded.Page ) ) throw new InvalidDataException( "Terrain snapshot contains duplicate pages." );
		}
		if ( stream.ReadByte() != -1 ) throw new InvalidDataException( "Terrain snapshot contains trailing data." );
		return new TerrainFieldSnapshot( settings, revision, pages, worldId );
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
