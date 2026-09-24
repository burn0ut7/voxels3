"""Publish a completed terrain bake without PNG encoding between asset updates."""
from io import BytesIO

from PIL import Image


def publish_bake(images, include_path, include_bytes):
    # Encode every image before modifying any engine-visible input. Encoding
    # interleaved with publication can exceed the engine's quiet-input timeout.
    encoded = {include_path: include_bytes}
    for path, pixels in images.items():
        buffer = BytesIO()
        Image.fromarray(pixels).save(buffer, format="PNG", compress_level=6)
        encoded[path] = buffer.getvalue()
    changed = {path: data for path, data in encoded.items()
               if not path.exists() or path.read_bytes() != data}
    staged = {}
    for path, data in changed.items():
        temporary = path.with_name(path.name + ".bake-tmp")
        temporary.write_bytes(data)
        staged[path] = temporary
    # This is a short publication burst, not an atomic multi-file transaction.
    for path, temporary in staged.items():
        try:
            temporary.replace(path)
        except PermissionError:
            # Do not manipulate applications or handles. Ordinary file access
            # is the only fallback; propagate failure if writing is denied.
            path.write_bytes(changed[path])
            temporary.unlink()
