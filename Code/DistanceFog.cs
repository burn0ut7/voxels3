using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Two-color atmospheric haze that becomes opaque at the rendered camera's far plane.
/// </summary>
[Title( "Distance Fog" ), Category( "Rendering" ), Icon( "foggy" )]
public sealed class DistanceFog : BasePostProcess<DistanceFog>
{
	/// <summary>Blue haze at the beginning of the distance fade. Alpha is ignored.</summary>
	[Property]
	public Color NearColor { get; set; } = new( 0.27f, 0.49f, 0.72f );

	/// <summary>Opaque fog and background color at the camera far plane. Alpha is ignored.</summary>
	[Property]
	public Color FarColor { get; set; } = new( 0.60f, 0.74f, 0.87f );

	/// <summary>Fraction of camera ZFar where fog starts. Full fog always occurs at ZFar.</summary>
	[Property, Range( 0f, 0.95f )]
	public float StartFraction { get; set; } = 0.08f;

	private Material _material;

	public override void Render()
	{
		Attributes.Set( "FogNearColor", GetWeighted( x => x.NearColor ) );
		Attributes.Set( "FogFarColor", GetWeighted( x => x.FarColor ) );
		var start = GetWeighted( x => x.StartFraction );
		Attributes.Set( "FogStartFraction", float.IsFinite( start ) ? start.Clamp( 0f, 0.95f ) : 0.08f );

		_material ??= Material.FromShader( "shaders/distance_fog.shader" );
		Blit( BlitMode.Simple( _material, Stage.BeforePostProcess, 0 ), "Distance Fog" );
	}
}
