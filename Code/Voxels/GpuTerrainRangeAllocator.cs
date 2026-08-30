using System;

internal sealed class GpuTerrainRangeAllocator
{
	private readonly List<GpuTerrainRange> _free = new();

	public int Capacity { get; }
	public int FreeCount => _free.Sum( range => range.Count );
	public int UsedCount => Capacity - FreeCount;
	public int LargestFreeRange => _free.Count == 0 ? 0 : _free.Max( range => range.Count );
	public int FreeRangeCount => _free.Count;

	public GpuTerrainRangeAllocator( int capacity )
	{
		Capacity = capacity;
		_free.Add( new GpuTerrainRange( 0, capacity ) );
	}

	public bool TryAllocate( int count, out GpuTerrainRange allocation )
	{
		if ( count == 0 )
		{
			allocation = default;
			return true;
		}
		for ( var index = 0; index < _free.Count; index++ )
		{
			var range = _free[index];
			if ( range.Count < count )
				continue;
			allocation = new GpuTerrainRange( range.Offset, count );
			if ( range.Count == count )
				_free.RemoveAt( index );
			else
				_free[index] = new GpuTerrainRange( range.Offset + count, range.Count - count );
			return true;
		}
		allocation = default;
		return false;
	}

	public void Release( GpuTerrainRange range )
	{
		if ( range.IsEmpty )
			return;
		if ( range.Offset < 0 || range.End > Capacity )
			throw new ArgumentOutOfRangeException( nameof( range ) );
		_free.Add( range );
		_free.Sort( (left, right) => left.Offset.CompareTo( right.Offset ) );
		for ( var index = _free.Count - 1; index > 0; index-- )
		{
			var previous = _free[index - 1];
			var current = _free[index];
			if ( previous.End > current.Offset )
				throw new InvalidOperationException( "GPU terrain ranges overlap." );
			if ( previous.End != current.Offset )
				continue;
			_free[index - 1] = new GpuTerrainRange( previous.Offset, previous.Count + current.Count );
			_free.RemoveAt( index );
		}
	}
}
