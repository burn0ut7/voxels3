using System;
using Sandbox.Rendering;

namespace Sandbox;

/// <summary>A drifting volumetric sky layer, rendered after distance fog.</summary>
[Title( "Volumetric Clouds" ), Category( "Rendering" ), Icon( "cloud" )]
public sealed class VolumetricClouds : BasePostProcess<VolumetricClouds>
{
	[Property, Range( 0f, 1f )]
	public float Coverage { get; set; } = 0.32f;

	[Property, Range( 100f, 4000f )]
	public float BaseHeightMeters { get; set; } = 1000f;

	/// <summary>Maximum elevation difference above and below the nominal base between cloud groups.</summary>
	[Property, Range( 0f, 800f )]
	public float BaseHeightVariationMeters { get; set; } = 250f;

	[Property, Range( 50f, 2000f )]
	public float ThicknessMeters { get; set; } = 900f;

	/// <summary>World-space repeat distance of the base noise, in meters.</summary>
	[Property, Range( 500f, 16000f )]
	public float ShapeScaleMeters { get; set; } = 2400f;

	/// <summary>Extinction per meter of full-density cloud.</summary>
	[Property, Range( 0.001f, 0.08f )]
	public float Density { get; set; } = 0.08f;

	[Property, Range( 0f, 0.6f )]
	public float Erosion { get; set; } = 0.22f;

	/// <summary>Horizontal wind in meters per second.</summary>
	[Property]
	public Vector2 Wind { get; set; } = new( 8f, 3f );

	[Property]
	public DirectionalLight Sun { get; set; }

	[Property]
	public DistanceFog Fog { get; set; }

	/// <summary>Minimum view samples; long horizon rays adapt up to 256 samples.</summary>
	[Property, Range( 16, 96 )]
	public int ViewSteps { get; set; } = 64;

	/// <summary>Divide both output dimensions by this amount before tracing.</summary>
	[Property, Range( 2, 4 )]
	public int ResolutionDivisor { get; set; } = 4;

	[Property, Range( 4000f, 48000f )]
	public float MaximumDistanceMeters { get; set; } = 24000f;

	// s&box world units are inches. Density, height and wind use meters throughout the shader.
	private const float MetersPerWorldUnit = 0.0254f;
	private Texture _shape;
	private Texture _erosion;
	private Texture _layout;
	private Material _traceMaterial;
	private Material _compositeMaterial;

	public override void Render()
	{
		var coverage = GetWeighted( x => x.Coverage );
		coverage = float.IsFinite( coverage ) ? coverage.Clamp( 0f, 1f ) : 0.32f;
		if ( coverage <= 0f || Camera.Orthographic ) return;

		_shape ??= Texture.CreateVolume( 128, 128, 128, ImageFormat.I8 )
			.WithData( FileSystem.Mounted.ReadAllBytes( "textures/clouds/shape.bin" ).ToArray() )
			.WithName( "cloud_shape" ).Finish();
		_erosion ??= Texture.CreateVolume( 32, 32, 32, ImageFormat.RGBA8888 )
			.WithData( FileSystem.Mounted.ReadAllBytes( "textures/clouds/erosion.bin" ).ToArray() )
			.WithName( "cloud_erosion" ).Finish();
		_layout ??= Texture.Create( 256, 256, ImageFormat.RGBA8888 )
			.WithData( FileSystem.Mounted.ReadAllBytes( "textures/clouds/layout.bin" ).ToArray() )
			.WithName( "cloud_layout" ).Finish();
		_traceMaterial ??= Material.FromShader( "shaders/clouds/volumetric_clouds.shader" );
		_compositeMaterial ??= Material.FromShader( "shaders/clouds/volumetric_clouds_composite.shader" );

		var height = GetWeighted( x => x.BaseHeightMeters );
		var heightVariation = GetWeighted( x => x.BaseHeightVariationMeters );
		var thickness = GetWeighted( x => x.ThicknessMeters );
		var scale = GetWeighted( x => x.ShapeScaleMeters );
		var density = GetWeighted( x => x.Density );
		var erosion = GetWeighted( x => x.Erosion );
		var distance = GetWeighted( x => x.MaximumDistanceMeters );
		var wind = GetWeighted( x => x.Wind );
		height = float.IsFinite( height ) ? height.Clamp( 100f, 4000f ) : 1000f;
		heightVariation = float.IsFinite( heightVariation ) ? heightVariation : 250f;
		heightVariation = heightVariation.Clamp( 0f, Math.Min( 800f, height - 100f ) );
		thickness = float.IsFinite( thickness ) ? thickness.Clamp( 50f, 2000f ) : 900f;
		scale = float.IsFinite( scale ) ? scale.Clamp( 500f, 16000f ) : 2400f;
		density = float.IsFinite( density ) ? density.Clamp( 0.001f, 0.08f ) : 0.08f;
		erosion = float.IsFinite( erosion ) ? erosion.Clamp( 0f, 0.6f ) : 0.22f;
		distance = float.IsFinite( distance ) ? distance.Clamp( 4000f, 48000f ) : 24000f;
		wind.x = float.IsFinite( wind.x ) ? wind.x.Clamp( -100f, 100f ) : 8f;
		wind.y = float.IsFinite( wind.y ) ? wind.y.Clamp( -100f, 100f ) : 3f;

		Attributes.Set( "CloudShape", _shape );
		Attributes.Set( "CloudErosion", _erosion );
		Attributes.Set( "CloudLayout", _layout );
		Attributes.Set( "CloudLayer", new Vector4( height, thickness, scale, distance ) );
		Attributes.Set( "CloudBaseVariation", heightVariation );
		Attributes.Set( "CloudShapeParameters", new Vector4( coverage, density, erosion, Math.Clamp( ViewSteps, 16, 96 ) ) );
		Attributes.Set( "CloudWind", wind * Time.Now );
		Attributes.Set( "CloudMetersPerUnit", MetersPerWorldUnit );
		Attributes.Set( "CloudSunDirection", Sun.IsValid() ? -Sun.WorldRotation.Forward : new Vector3( -0.5f, -0.5f, 0.7071068f ) );
		Attributes.Set( "CloudSunColor", Sun.IsValid() && Sun.Enabled ? Sun.LightColor : Color.Black );
		Attributes.Set( "CloudHazeColor", Fog.IsValid() ? Fog.FarColor : Camera.BackgroundColor );

		var commands = new CommandList( "Volumetric Clouds" );
		var target = commands.GetRenderTarget( "CloudScattering", ImageFormat.RGBA16161616F,
			sizeFactor: Math.Clamp( ResolutionDivisor, 2, 4 ) );
		commands.SetRenderTarget( target );
		commands.Blit( _traceMaterial, Attributes );
		commands.ClearRenderTarget();
		commands.Attributes.Set( "CloudScattering", target.ColorTexture );
		commands.Blit( _compositeMaterial );
		commands.ReleaseRenderTarget( target );
		InsertCommandList( commands, Stage.BeforePostProcess, 10, "Volumetric Clouds" );
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		_shape?.Dispose();
		_erosion?.Dispose();
		_layout?.Dispose();
		_shape = null;
		_erosion = null;
		_layout = null;
	}
}
