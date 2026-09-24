using System;
using System.Linq;

/// <summary>Visual distance transition for an authored tree; solid physics stays independent.</summary>
public sealed class TreeModelLod : Component
{
	// The editor supplies its detached render camera. Standalone play uses Scene.Camera.
	// This is transient view state, never serialized into a tree or replicated.
	public static CameraComponent EditorCamera { get; set; }
	[Property] public string Specimen { get; set; }
	[Property] public float DetailReturnMeters { get; set; } = 12f;
	[Property] public float DetailExitMeters { get; set; } = 16f;
	[Property] public float TransitionSeconds { get; set; } = 0.35f;
	[Property, ReadOnly] public float DetailFraction { get; private set; } = 1f;
	[Property, ReadOnly] public float ViewDistanceMeters { get; private set; }
	[Property, ReadOnly] public string Status { get; private set; } = "Waiting";
	private ModelRenderer[] _renderers;
	private Model[] _models;
	private bool[] _batchable;
	private bool[] _foliageOnly;
	private int?[] _lodOverrides;
	private ModelRenderer.ShadowRenderType[] _shadowTypes;
	private SceneObject _far;
	private SceneObject _shadow;
	private string _loadedKey;
	private bool _failed;
	private bool _farSelected;
	private float _detail = float.NaN;

	protected override void OnPreRender()
	{
		var camera = EditorCamera.IsValid() && EditorCamera.Scene == Scene ? EditorCamera : Scene.Camera;
		if ( !Game.IsPlaying || !camera.IsValid() ) return;
		if ( _loadedKey != Specimen || (_far is not null && (_lodOverrides is null || _shadowTypes is null || _foliageOnly is null)) ) Restore();
		_loadedKey = Specimen;
		if ( _failed || string.IsNullOrEmpty( Specimen ) ) return;
		try
		{
			if ( _renderers is not null )
			{
				var activeCount = 0;
				foreach ( var renderer in GetComponentsInChildren<ModelRenderer>( true ) )
				{
					if ( !renderer.Active ) continue;
					activeCount++;
					if ( Array.IndexOf( _renderers, renderer ) < 0 )
						throw new InvalidOperationException( "Tree render membership changed." );
				}
				if ( activeCount != _renderers.Length )
					throw new InvalidOperationException( "Tree render membership changed." );
				for ( var index = 0; index < _renderers.Length; index++ )
				{
					var renderer = _renderers[index];
					if ( renderer.Model != _models[index] ||
						(renderer.WorldPosition - WorldPosition).Length > 0.01f ||
						(renderer.WorldScale - WorldScale).Length > 0.001f || renderer.WorldRotation != WorldRotation )
						throw new InvalidOperationException( "Tree render source changed." );
				}
			}
			if ( _far is null )
			{
				var renderers = GetComponentsInChildren<ModelRenderer>( true ).Where( x => x.Active ).ToArray();
				if ( renderers.Length == 0 || renderers.Any( x => !x.SceneObject.IsValid() ) ) return;
				var minimum = new Vector3( float.MaxValue );
				var maximum = new Vector3( float.MinValue );
				foreach ( var renderer in renderers )
				{
					if ( renderer.Model is null || renderer.Model.IsError ) return;
					// Installed multipart exports share the prefab origin and unit transform.
					if ( (renderer.WorldPosition - WorldPosition).Length > 0.01f ||
						(renderer.WorldScale - WorldScale).Length > 0.001f || renderer.WorldRotation != WorldRotation )
						throw new InvalidOperationException( "Tree render pieces must share their root transform." );
					minimum = Vector3.Min( minimum, renderer.Model.Bounds.Mins );
					maximum = Vector3.Max( maximum, renderer.Model.Bounds.Maxs );
				}
				var models = renderers.Select( x => x.Model ).ToArray();
				var model = TreeDistantModel.Load( Specimen, new BBox( minimum, maximum ), models, out var foliageModels );
				if ( model is null ) throw new InvalidOperationException( "Distant tree bake is missing." );
				_renderers = renderers;
				_models = models;
				_foliageOnly = models.Select( foliageModels.Contains ).ToArray();
				_batchable = renderers.Select( x => x.SceneObject.Batchable ).ToArray();
				_lodOverrides = renderers.Select( x => x.LodOverride ).ToArray();
				_shadowTypes = renderers.Select( x => x.RenderType ).ToArray();
				_far = new SceneObject( Scene.SceneWorld, model, WorldTransform );
				_far.Batchable = false;
				_far.RenderingEnabled = false;
				_far.Flags.CastShadows = false;
				// Only the distant representation uses the baked shadow silhouette.
				// Nearby leaves and wood must shadow their actual animated geometry.
				_shadow = new SceneObject( Scene.SceneWorld, model, WorldTransform );
				_shadow.Batchable = false;
				_shadow.Flags.CastShadows = true;
				_shadow.Flags.ExcludeGameLayer = true;
				_shadow.Flags.WantsPrePass = false;
				_shadow.Attributes.Set( "TreeLodFade", 0f );
				_shadow.RenderingEnabled = _shadowTypes.Any( x => x != ModelRenderer.ShadowRenderType.Off );
			}
			var scale = WorldScale;
			if ( scale.x <= 0f || Math.Abs( scale.y - scale.x ) > 0.001f || Math.Abs( scale.z - scale.x ) > 0.001f )
				throw new InvalidOperationException( "Tree LOD requires positive uniform scale." );
			ViewDistanceMeters = (camera.WorldPosition - WorldPosition).Length * 0.0254f / scale.x;
			var returnDistance = Math.Max( 1f, DetailReturnMeters );
			var exitDistance = Math.Max( returnDistance + 1f, DetailExitMeters );
			var step = Math.Min( RealTime.Delta / Math.Max( 0.01f, TransitionSeconds ), 1f );
			_detail = TreeLodTransition.Advance( _detail, ref _farSelected, ViewDistanceMeters,
				returnDistance, exitDistance, step, step );
			DetailFraction = _detail;
			_far.Transform = WorldTransform;
			_shadow.Transform = WorldTransform;
			_far.Attributes.Set( "TreeLodFade", _detail );
			_far.RenderingEnabled = _detail < 1f;
			_far.Batchable = _detail == 0f;
			_shadow.Batchable = _detail == 0f;
			_shadow.Attributes.Set( "TreeLodFade", _detail );
			_shadow.Attributes.SetCombo( "D_TREE_SHADOW", 1 );
			_shadow.RenderingEnabled = _detail < 1f && _shadowTypes.Any( x => x != ModelRenderer.ShadowRenderType.Off );
			// Foliage-only pieces use two-triangle leaf cards with the same cutout
			// texture, attachment and camera-facing wind animation at every distance.
			// Keep wood detail independent; mixed legacy models retain their full LOD range.
			var meshLod = ViewDistanceMeters < 3f ? 0 : ViewDistanceMeters < 6f ? 1 : 2;
			for ( var index = 0; index < _renderers.Length; index++ )
			{
				var renderer = _renderers[index];
				if ( !renderer.IsValid() || !renderer.SceneObject.IsValid() ) continue;
				renderer.LodOverride = _lodOverrides[index] ?? Math.Max( meshLod, _foliageOnly[index] ? 2 : 0 );
				renderer.RenderType = _shadowTypes[index];
				renderer.SceneObject.Batchable = false;
				renderer.SceneObject.Attributes.Set( "TreeLodFade", _detail );
				renderer.SceneObject.Attributes.Set( "TreeLeafView", new Vector4( camera.WorldPosition, 1f ) );
				renderer.SceneObject.Attributes.Set( "TreeLeafViewForward", camera.WorldRotation.Forward );
				renderer.SceneObject.Attributes.Set( "TreeLeafViewUp", camera.WorldRotation.Up );
				renderer.SceneObject.RenderingEnabled = _detail > 0f;
			}
			Status = _detail == 0f ? "Distant" : _detail == 1f ? "Detailed" : "Transition";
		}
		catch ( Exception exception )
		{
			Restore();
			_failed = true;
			Status = "Detailed fallback";
			Log.Error( $"[TreeModelLod] {Specimen}: {exception.Message}" );
		}
	}

	/// <summary>Keep the currently selected detailed meshes while physics takes ownership of the tree pose.</summary>
	public void StopDistanceTransitions()
	{
		var renderers = GetComponentsInChildren<ModelRenderer>( true ).ToArray();
		var levels = renderers.Select( renderer => renderer.LodOverride ).ToArray();
		Enabled = false;
		for ( var i = 0; i < renderers.Length; i++ ) renderers[i].LodOverride = levels[i];
	}

	private void Restore()
	{
		_far?.Delete();
		_far = null;
		_shadow?.Delete();
		_shadow = null;
		if ( _renderers is not null )
			for ( var index = 0; index < _renderers.Length; index++ )
			{
				var renderer = _renderers[index];
				if ( !renderer.IsValid() || !renderer.SceneObject.IsValid() ) continue;
				if ( _lodOverrides is not null ) renderer.LodOverride = _lodOverrides[index];
				if ( _shadowTypes is not null ) renderer.RenderType = _shadowTypes[index];
				renderer.SceneObject.RenderingEnabled = true;
				renderer.SceneObject.Attributes.Set( "TreeLodFade", 1f );
				renderer.SceneObject.Attributes.Set( "TreeLeafView", Vector4.Zero );
				renderer.SceneObject.Batchable = _batchable[index];
			}
		_renderers = null;
		_models = null;
		_batchable = null;
		_foliageOnly = null;
		_lodOverrides = null;
		_shadowTypes = null;
		_detail = float.NaN;
		DetailFraction = 1f;
		Status = "Waiting";
		_failed = false;
	}

	protected override void OnDisabled() => Restore();
	protected override void OnDestroy() => Restore();
}
