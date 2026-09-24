using System;
using Sandbox.UI;

namespace Sandbox;

public enum BiomeDebugView { Off, Readout, Map }
public enum BiomeMapLayer { Biomes, Blends, Temperature, Humidity }

/// <summary>A map image with normalized click coordinates; world interpretation stays in its owner.</summary>
public sealed class BiomeMapImage : Image
{
	public Action<Vector2> Selected { get; set; }
	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );
		if ( Box.Rect.Width <= 0 || Box.Rect.Height <= 0 ) return;
		Selected?.Invoke( new Vector2( Math.Clamp( e.LocalPosition.x / Box.Rect.Width, 0f, 1f ),
			Math.Clamp( e.LocalPosition.y / Box.Rect.Height, 0f, 1f ) ) );
	}
}
