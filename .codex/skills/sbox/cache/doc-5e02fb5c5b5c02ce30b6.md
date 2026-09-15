# Post Processing

Post-processing effects can be applied by adding components to the Camera or to a [PostProcessVolume](/dev/doc/rendering/post-processing/postprocessvolume).

## [Effects](/dev/doc/rendering/post-processing/effects)

The built in effects: tonemapping and film grain.

## [PostProcessVolume](/dev/doc/rendering/post-processing/postprocessvolume)

Apply a set of effects only while the camera is inside a volume, with blending.

## [Creating PostProcesses](/dev/doc/rendering/post-processing/creating-postprocesses)

Write your own effect as a component plus a shader.

# Camera Settings

The Camera component has settings especially for post processing.

### EnablePostProcessing

Disable to prevent post processing from rendering on this camera at all.

### PostProcessAnchor

By default when triggering `PostProcessVolume`'s we use the camera's position. 

This isn't always the behaviour you want. For example, if you're making a top down game, you probably want it to use the player's position.

If this is set, we'll use the world position of that GameObject to find PostProcessVolume's instead of the Camera's position.
