using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

/// <summary>
/// Host checkpoint storage for canonical page versions. Runtime I/O uses workers;
/// normal teardown flushes synchronously. Live commits remain owned by TerrainField.
/// Completion records publish immutable indexes after all page writes close.
/// Stream completion is not a power-loss durability guarantee.
/// </summary>
internal static class TerrainFieldStore
{
	// Recipe-specific selectors preserve earlier generators and independently tuned worlds.
	private static string SelectionPath( ProceduralTerrainSettings settings )
	{
		using var identity = new MemoryStream();
		TerrainFieldCodec.WriteIdentity( identity, new TerrainFieldIdentity( settings, 0, Guid.Empty ) );
		return $"terrain/last-world-v{ProceduralTerrainSdf.CurrentVersion}-{Convert.ToHexString( SHA256.HashData( identity.ToArray() ) )}.vxl";
	}

	public static Checkpoint OpenLast( ProceduralTerrainSettings settings, CancellationToken cancellation )
	{
		lock ( IoGate )
		{
			var selectionPath = SelectionPath( settings );
			if ( !FileSystem.Data.FileExists( selectionPath ) ) return null;
			var bytes = ReadBounded( selectionPath, 64 );
			if ( bytes.Length <= 32 || !SHA256.HashData( bytes.AsSpan( 32 ) ).AsSpan().SequenceEqual( bytes.AsSpan( 0, 32 ) ) )
				throw new InvalidDataException( "The last-world selection is corrupt; refusing to replace it with a fresh world." );
			return Open( RootPath( Encoding.UTF8.GetString( bytes, 32, bytes.Length - 32 ) ), settings, cancellation );
		}
	}
	private const int IndexMagic = 0x31535856;
	private const int IndexVersion = 1;
	private const int MaximumIndexBytes = 32 + TerrainFieldCodec.IdentityBytes + TerrainField.MaximumPages * 52;
	private const long MaximumStoreBytes = 1L << 30;
	private const int MaximumStoredFiles = TerrainField.MaximumPages * 3 + 6;
	// Serializes checkpoint cleanup, reads, and the final synchronous teardown save.
	private static readonly object IoGate = new();
	// Created only under IoGate. Live metadata/read handles protect their exact
	// immutable page file across checkpoint cleanup without retaining sample arrays.
	private static readonly Dictionary<(string Root, string Hash), WeakReference<Page>> FileReferences = new();

	internal sealed record Page( Vector3Int Coordinate, int Revision, int Bytes, string Hash );
	internal sealed record Checkpoint( string Root, long Sequence, TerrainFieldIdentity Identity,
		IReadOnlyDictionary<Vector3Int, Page> Pages, int WrittenPages = 0,
		long PageBytesRead = 0, long PageBytesWritten = 0 );

	private static Page ReferencePage( string root, Vector3Int coordinate, int revision, int bytes, string hash )
	{
		var key = (root, hash);
		if ( FileReferences.TryGetValue( key, out var reference ) && reference.TryGetTarget( out var existing ) )
		{
			if ( existing.Coordinate != coordinate || existing.Revision != revision || existing.Bytes != bytes )
				throw new InvalidDataException( "Terrain stored page metadata conflicts with its content identity." );
			return existing;
		}
		if ( FileReferences.Count >= MaximumStoredFiles )
		{
			foreach ( var pair in FileReferences.Where( pair => !pair.Value.TryGetTarget( out _ ) ).ToArray() ) FileReferences.Remove( pair.Key );
			if ( FileReferences.Count >= MaximumStoredFiles ) throw new IOException( "Terrain stored-version reference budget exhausted." );
		}
		var page = new Page( coordinate, revision, bytes, hash );
		FileReferences[key] = new WeakReference<Page>( page );
		return page;
	}

	public static string RootPath( string slot )
	{
		if ( string.IsNullOrWhiteSpace( slot ) || slot.Length > 32 ||
			slot.Any( character => !(character >= 'a' && character <= 'z') &&
				!(character >= '0' && character <= '9') && character != '-' && character != '_' ) )
			throw new ArgumentException( "Save names use 1-32 lowercase letters, digits, hyphens or underscores." );
		return $"terrain/{slot}";
	}

	public static Checkpoint Save( string root, TerrainFieldSnapshot source, CancellationToken cancellation )
	{
		if ( source.IsRegional ) throw new InvalidOperationException( "Cannot save a regional reader as a world." );
		lock ( IoGate )
		{
			var fs = FileSystem.Data;
			fs.CreateDirectory( root );
			fs.CreateDirectory( $"{root}/pages" );
			fs.CreateDirectory( $"{root}/checkpoints" );
			var completed = CompletedSequences( root );
			var previous = completed.Length == 0 ? null : ReadIndex( root, completed[0], source.Settings, cancellation );
			if ( previous is not null && previous.Identity.WorldId != source.WorldId )
				throw new InvalidOperationException( "Save slot belongs to another world. Open it before editing, or choose a new slot." );
			if ( previous is not null && previous.Identity.Revision > source.Revision )
				throw new InvalidOperationException( "A newer world revision is already saved." );
			// Before beginning a new candidate, retain the latest complete checkpoint.
			// It remains recoverable through every subsequent write failure.
			CleanUnused( root, previous, cancellation );
			var sequence = checked( (previous?.Sequence ?? 0) + 1 );
			var pages = new Dictionary<Vector3Int, Page>();
			var usage = StoreUsage( root );
			var storedBytes = usage.Bytes;
			var storedFiles = usage.Files;
			var writtenPages = 0;
			long pageBytesRead = 0, pageBytesWritten = 0;
			foreach ( var pair in source.Pages.OrderBy( pair => pair.Key.z ).ThenBy( pair => pair.Key.y ).ThenBy( pair => pair.Key.x ) )
			{
				cancellation.ThrowIfCancellationRequested();
				var stored = pair.Value.Stored;
				byte[] block;
				Page page;
				var destinationVerified = false;
				if ( stored.Page is not null )
				{
					if ( stored.Page.Coordinate != pair.Key || stored.Page.Revision != pair.Value.Revision )
						throw new InvalidDataException( "Stored terrain page does not match its canonical version." );
					block = ReadPageBytes( stored.Root, stored.Page );
					pageBytesRead += block.Length;
					page = ReferencePage( root, pair.Key, stored.Page.Revision, stored.Page.Bytes, stored.Page.Hash );
					destinationVerified = stored.Root == root;
				}
				else
				{
					block = TerrainFieldCodec.EncodePageBlock( pair.Key, PinForRead( pair.Value ) );
					page = ReferencePage( root, pair.Key, pair.Value.Revision, block.Length, Convert.ToHexString( SHA256.HashData( block ) ) );
				}
				var path = $"{root}/pages/{page.Hash}.vxp";
				if ( !destinationVerified && fs.FileExists( path ) )
				{
					pageBytesRead += ReadPageBytes( root, page ).Length;
					destinationVerified = true;
				}
				if ( !destinationVerified )
				{
					if ( storedBytes + block.Length + MaximumIndexBytes + 32 > MaximumStoreBytes || storedFiles + 3 > MaximumStoredFiles )
						throw new IOException( "Terrain store disk budget exhausted." );
					WriteNew( path, block );
					storedBytes += block.Length;
					storedFiles++;
					writtenPages++;
					pageBytesWritten += block.Length;
				}
				pages.Add( pair.Key, page );
			}
			var identity = new TerrainFieldIdentity( source.Settings, source.Revision, source.WorldId, source.Epoch );
			var checkpoint = new Checkpoint( root, sequence, identity, pages, writtenPages, pageBytesRead, pageBytesWritten );
			var index = EncodeIndex( checkpoint );
			if ( storedBytes + index.Length + 32 > MaximumStoreBytes || storedFiles + 2 > MaximumStoredFiles )
				throw new IOException( "Terrain checkpoint exceeds the store budget." );
			var stem = $"{root}/checkpoints/{sequence.ToString( "D20", CultureInfo.InvariantCulture )}";
			cancellation.ThrowIfCancellationRequested();
			WriteNew( stem + ".vxi", index );
			cancellation.ThrowIfCancellationRequested();
			WriteNew( stem + ".vxc", SHA256.HashData( index ) );
			var slot = Encoding.UTF8.GetBytes( root["terrain/".Length..] );
			using ( var selection = fs.OpenWrite( SelectionPath( source.Settings ), FileMode.Create ) )
			{
				selection.Write( SHA256.HashData( slot ) );
				selection.Write( slot );
				selection.Flush();
			}
			// No cancellation point after publication: this exact revision completed.
			return checkpoint;
		}
	}

	public static Checkpoint Open( string root, ProceduralTerrainSettings settings, CancellationToken cancellation )
	{
		lock ( IoGate )
		{
			var sequences = CompletedSequences( root );
			if ( sequences.Length == 0 ) throw new FileNotFoundException( "No completed terrain checkpoint exists." );
			var checkpoint = ReadIndex( root, sequences[0], settings, cancellation );
			// ReadDirectory validates every referenced page before replacement can
			// commit. A corrupt completed checkpoint never falls back to old history.
			if ( FileSystem.Data.FindFile( $"{root}/checkpoints", "*.vxi" ).Count() > sequences.Length )
				Log.Warning( "[TerrainStorage] ignored incomplete checkpoint candidate" );
			return checkpoint;
		}
	}

	public static TerrainFieldPage ReadPage( string root, Page page, TerrainFieldPage.SampleReservation reservation = null, float[] metadataScratch = null )
	{
		lock ( IoGate )
		{
			var decoded = TerrainFieldCodec.DecodePageBlock( ReadPageBytes( root, page ), page.Revision, reservation, metadataScratch );
			if ( decoded.Coordinate != page.Coordinate || decoded.Page.Revision != page.Revision )
				throw new InvalidDataException( "Stored terrain page identity does not match its directory." );
			decoded.Page.AttachStored( root, page );
			return decoded.Page;
		}
	}

	// Worker-only consumers of a fixed historical version use the same backing
	// record without installing it into a newer authoritative directory.
	public static TerrainFieldPage PinForRead( TerrainFieldPage page )
	{
		if ( page.TryPin( out var reader ) ) return reader;
		var stored = page.Stored;
		if ( stored.Page is null ) throw new InvalidOperationException( "Terrain page has no recoverable saved samples." );
		var loaded = ReadPage( stored.Root, stored.Page );
		loaded.TryPin( out reader );
		return reader;
	}

	public static byte[] EncodePageVersion( Vector3Int coordinate, TerrainFieldPage page )
	{
		lock ( IoGate )
		{
			var stored = page.Stored;
			if ( stored.Page is null ) return TerrainFieldCodec.EncodePageBlock( coordinate, PinForRead( page ) );
			if ( stored.Page.Coordinate != coordinate ) throw new InvalidDataException( "Stored terrain coordinate does not match the requested page." );
			return ReadPageBytes( stored.Root, stored.Page );
		}
	}

	public static TerrainFieldSnapshot ReadDirectory( Checkpoint checkpoint, CancellationToken cancellation )
	{
		lock ( IoGate )
		{
			var pages = new Dictionary<Vector3Int, TerrainFieldPage>();
			// Validation builds only canonical metadata. One budgeted scratch array
			// serves the whole directory; no page or weak reuse handle retains it.
			var scratch = checkpoint.Pages.Count == 0 ? null : TerrainFieldPage.AllocateValues();
			foreach ( var page in checkpoint.Pages.Values )
			{
				cancellation.ThrowIfCancellationRequested();
				var decoded = ReadPage( checkpoint.Root, page, metadataScratch: scratch );
				pages.Add( page.Coordinate, decoded );
			}
			return new TerrainFieldSnapshot( checkpoint.Identity.Settings, checkpoint.Identity.Revision, pages, checkpoint.Identity.WorldId );
		}
	}

	private static long[] CompletedSequences( string root )
	{
		if ( !FileSystem.Data.DirectoryExists( $"{root}/checkpoints" ) ) return Array.Empty<long>();
		var names = FileSystem.Data.FindFile( $"{root}/checkpoints", "*.vxc" ).Take( 4 ).ToArray();
		if ( names.Length > 3 ) throw new InvalidDataException( "Terrain checkpoint directory exceeds its budget." );
		var sequences = names.Where( name =>
		{
			var bytes = FileSystem.Data.FileSize( $"{root}/checkpoints/{Path.GetFileName( name )}" );
			if ( bytes > 32 ) throw new InvalidDataException( "Invalid terrain completion record." );
			if ( bytes == 32 ) return true;
			Log.Warning( "[TerrainStorage] ignored incomplete checkpoint completion record" );
			return false;
		} ).Select( name =>
		{
			var stem = Path.GetFileNameWithoutExtension( name );
			if ( stem.Length != 20 || !long.TryParse( stem, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence ) || sequence < 1 )
				throw new InvalidDataException( "Invalid terrain checkpoint name." );
			return sequence;
		} ).OrderByDescending( sequence => sequence ).ToArray();
		if ( sequences.Length > 2 ) throw new InvalidDataException( "Terrain completed checkpoint budget exceeded." );
		return sequences;
	}

	private static byte[] EncodeIndex( Checkpoint checkpoint )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream, Encoding.UTF8, true );
		writer.Write( IndexMagic ); writer.Write( IndexVersion ); writer.Write( checkpoint.Sequence );
		using var identity = new MemoryStream();
		TerrainFieldCodec.WriteIdentity( identity, checkpoint.Identity );
		writer.Write( (int)identity.Length ); writer.Write( identity.ToArray() );
		writer.Write( checkpoint.Pages.Count );
		foreach ( var page in checkpoint.Pages.Values )
		{
			writer.Write( page.Coordinate.x ); writer.Write( page.Coordinate.y ); writer.Write( page.Coordinate.z );
			writer.Write( page.Revision ); writer.Write( page.Bytes ); writer.Write( Convert.FromHexString( page.Hash ) );
		}
		return stream.ToArray();
	}

	private static Checkpoint ReadIndex( string root, long sequence, ProceduralTerrainSettings settings, CancellationToken cancellation )
	{
		var stem = $"{root}/checkpoints/{sequence.ToString( "D20", CultureInfo.InvariantCulture )}";
		var block = ReadBounded( stem + ".vxi", MaximumIndexBytes );
		var completion = ReadBounded( stem + ".vxc", 32 );
		if ( completion.Length != 32 || !SHA256.HashData( block ).AsSpan().SequenceEqual( completion ) )
			throw new InvalidDataException( "Terrain checkpoint index is incomplete or corrupt." );
		using var stream = new MemoryStream( block, false );
		using var reader = new BinaryReader( stream );
		if ( reader.ReadInt32() != IndexMagic || reader.ReadInt32() != IndexVersion || reader.ReadInt64() != sequence )
			throw new InvalidDataException( "Unsupported terrain checkpoint index." );
		var identityBytes = reader.ReadInt32();
		if ( identityBytes != TerrainFieldCodec.IdentityBytes ) throw new InvalidDataException( "Invalid terrain checkpoint identity length." );
		using var identityStream = new MemoryStream( reader.ReadBytes( identityBytes ), false );
		var identity = TerrainFieldCodec.ReadIdentity( identityStream, settings );
		var count = reader.ReadInt32();
		if ( count < 0 || count > TerrainField.MaximumPages || stream.Length - stream.Position != count * 52L )
			throw new InvalidDataException( "Invalid terrain directory size." );
		var pages = new Dictionary<Vector3Int, Page>();
		for ( var i = 0; i < count; i++ )
		{
			cancellation.ThrowIfCancellationRequested();
			var key = new Vector3Int( reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32() );
			var revision = reader.ReadInt32(); var bytes = reader.ReadInt32();
			var hash = Convert.ToHexString( reader.ReadBytes( 32 ) );
			if ( revision < 1 || revision > identity.Revision || bytes < 53 || bytes > TerrainFieldCodec.MaximumPageBlockBytes ||
				!pages.TryAdd( key, ReferencePage( root, key, revision, bytes, hash ) ) )
				throw new InvalidDataException( "Invalid or duplicate terrain page directory entry." );
		}
		return new Checkpoint( root, sequence, identity, pages );
	}

	private static byte[] ReadPageBytes( string root, Page page )
	{
		var block = ReadBounded( $"{root}/pages/{page.Hash}.vxp", TerrainFieldCodec.MaximumPageBlockBytes );
		if ( block.Length != page.Bytes || Convert.ToHexString( SHA256.HashData( block ) ) != page.Hash )
			throw new InvalidDataException( "Terrain page content checksum failed." );
		return block;
	}

	private static byte[] ReadBounded( string path, int limit )
	{
		using var stream = FileSystem.Data.OpenRead( path );
		using var result = new MemoryStream();
		var buffer = new byte[8192];
		int read;
		while ( (read = stream.Read( buffer, 0, buffer.Length )) > 0 )
		{
			if ( result.Length + read > limit ) throw new InvalidDataException( "Terrain file exceeds its byte budget." );
			result.Write( buffer, 0, read );
		}
		return result.ToArray();
	}

	private static void WriteNew( string path, byte[] bytes )
	{
		using var stream = FileSystem.Data.OpenWrite( path, FileMode.CreateNew );
		stream.Write( bytes, 0, bytes.Length );
		stream.Flush();
	}

	private static (long Bytes, int Files) StoreUsage( string root )
	{
		var files = FileSystem.Data.FindFile( $"{root}/pages", "*" )
			.Select( file => $"{root}/pages/{Path.GetFileName( file )}" )
			.Concat( FileSystem.Data.FindFile( $"{root}/checkpoints", "*" )
				.Select( file => $"{root}/checkpoints/{Path.GetFileName( file )}" ) )
			.Take( MaximumStoredFiles + 1 ).ToArray();
		if ( files.Length > MaximumStoredFiles ) throw new IOException( "Terrain store file budget exhausted." );
		var bytes = files.Sum( file => FileSystem.Data.FileSize( file ) );
		if ( bytes > MaximumStoreBytes ) throw new IOException( "Terrain store disk budget exhausted." );
		return (bytes, files.Length);
	}

	private static void CleanUnused( string root, Checkpoint keep, CancellationToken cancellation )
	{
		var keepStem = keep?.Sequence.ToString( "D20", CultureInfo.InvariantCulture );
		foreach ( var file in FileSystem.Data.FindFile( $"{root}/checkpoints", "*" ).Take( 7 ).ToArray() )
		{
			cancellation.ThrowIfCancellationRequested();
			var name = Path.GetFileName( file );
			if ( Path.GetFileNameWithoutExtension( name ) == keepStem ) continue;
			FileSystem.Data.DeleteFile( $"{root}/checkpoints/{name}" );
		}
		var hashes = keep is null ? new HashSet<string>() : keep.Pages.Values.Select( page => page.Hash ).ToHashSet();
		foreach ( var pair in FileReferences.ToArray() )
		{
			if ( !pair.Value.TryGetTarget( out var page ) ) FileReferences.Remove( pair.Key );
			else if ( pair.Key.Root == root ) hashes.Add( page.Hash );
		}
		var files = FileSystem.Data.FindFile( $"{root}/pages", "*.vxp" ).Take( MaximumStoredFiles + 1 ).ToArray();
		if ( files.Length > MaximumStoredFiles ) throw new IOException( "Terrain store cleanup exceeds its file budget." );
		foreach ( var file in files )
		{
			cancellation.ThrowIfCancellationRequested();
			var name = Path.GetFileName( file );
			if ( !hashes.Contains( Path.GetFileNameWithoutExtension( name ) ) ) FileSystem.Data.DeleteFile( $"{root}/pages/{name}" );
		}
	}
}
