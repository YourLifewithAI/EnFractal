"""Pose backends. Today: MapAnything (Meta, 2025/2026) with the Apache-2.0 weights.

The backend turns a list of photos into, per photo, a camera-to-world pose, intrinsics and a
metric point map in the camera frame. Everything heavy (torch, the model) is imported lazily so
C1 and the unit tests never need the GPU stack.
"""

from __future__ import annotations

import contextlib
import math
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

# Every model the pipeline downloads, with its licence. Checked 6 October 2026 on Hugging Face.
MODEL_REGISTRY = {
    "facebook/map-anything-apache": {
        "role": "camera poses, intrinsics and metric depth (C2)",
        "licence": "Apache-2.0 (weights); code Apache-2.0",
        "gated": False,
        "revision": "00f9c245bbcb60522d1ed7f9e9d88462c6e3f38a",
        "download_bytes": 4914062480,
    },
    "google/owlv2-base-patch16-ensemble": {
        "role": "open-vocabulary object detection for per-object view counts (C2)",
        "licence": "Apache-2.0",
        "gated": False,
        "revision": "cfd3195ba4ea9592eec887ded089f4c08eff231d",
        "download_bytes": 619918824,
    },
}
# Code (not weights) fetched by torch.hub when MapAnything builds its DINOv2 encoder. Uniception
# asks for the repository's moving main branch; ``pinned_torch_hub`` pins it to this commit.
# The weights come from the MapAnything checkpoint above, so no DINOv2 weights are downloaded.
TORCH_HUB_PINS = {
    "facebookresearch/dinov2": {
        "ref": "7764ea0f912e53c92e82eb78a2a1631e92725fc8",
        "licence": "Apache-2.0 (code)",
        "role": "DINOv2 encoder definition used by MapAnything",
    },
}
# Considered and not used, recorded so the founder can decide later:
REJECTED_MODELS = {
    "facebook/VGGT-1B": "CC BY-NC 4.0 (non-commercial only)",
    "facebook/VGGT-1B-Commercial": "commercial licence, gated behind an application form on Hugging Face",
    "facebook/map-anything": "CC BY-NC 4.0 (non-commercial only); the -apache variant is used instead",
    "naver/MASt3R": "CC BY-NC-SA 4.0 (non-commercial only)",
}

POSE_MODEL = "facebook/map-anything-apache"
TARGET_SIZE = (392, 518)  # (width, height): MapAnything's 3:4 portrait size at the 518 setting
STORE_STRIDE = 2  # keep every 2nd pixel of the point maps on disk
HALF_MODULES = ("encoder", "info_sharing")
# fp32 but without cuDNN: its fp32 conv workspace reserves ~3 GB at 24 views for no benefit here.
NO_CUDNN_MODULES = ("ray_dirs_encoder", "depth_encoder")


@dataclass
class GpuStats:
    seconds: float = 0.0
    prepare_seconds: float = 0.0  # decoding and resizing photos for the model (CPU)
    peak_allocated_mib: float = 0.0
    peak_reserved_mib: float = 0.0
    batches: list[dict[str, Any]] = field(default_factory=list)


def nvidia_smi_memory() -> tuple[int, int] | None:
    """(used MiB, total MiB) of GPU 0 from nvidia-smi, or None if unavailable."""
    exe = shutil.which("nvidia-smi")
    if not exe:
        return None
    try:
        out = subprocess.run(
            [exe, "--query-gpu=memory.used,memory.total", "--format=csv,noheader,nounits"],
            capture_output=True, text=True, timeout=20, check=True,
        ).stdout.strip().splitlines()[0]
        used, total = (int(x.strip()) for x in out.split(","))
        return used, total
    except (subprocess.SubprocessError, ValueError, IndexError, OSError):
        return None


GAME_WINDOW_PREFIX = "EnFractal"


def game_window_open() -> bool:
    """Whether a window titled EnFractal... is open (the founder may be playing on this GPU).

    Windows only (``tasklist /v``); elsewhere, or when the check itself fails, returns False.
    """
    if not sys.platform.startswith("win"):
        return False
    try:
        out = subprocess.run(["tasklist", "/v", "/fo", "csv", "/nh"], capture_output=True, text=True,
                             timeout=30, check=True, errors="replace").stdout
    except (subprocess.SubprocessError, OSError):
        return False
    for line in out.splitlines():
        fields = [f.strip('"') for f in line.split('","')]
        if fields and fields[-1].startswith(GAME_WINDOW_PREFIX):
            return True
    return False


def wait_while_game_runs(*, retries: int = 240, delay_s: float = 30.0, log=print) -> float:
    """Pause GPU work while the game is open; returns the seconds waited."""
    waited = 0.0
    for attempt in range(retries):
        if not game_window_open():
            return waited
        if attempt == 0:
            log("The EnFractal game is open on this GPU; pausing GPU work until it closes...")
        time.sleep(delay_s)
        waited += delay_s
    raise RuntimeError("The game stayed open; GPU work was not started. Run the coverage step again later.")


def wait_for_vram(need_mib: int, *, retries: int = 20, delay_s: float = 30.0, log=print) -> int:
    """Wait (politely) until ``need_mib`` is free; other lanes may be capturing on this GPU."""
    wait_while_game_runs(log=log)
    for attempt in range(retries + 1):
        mem = nvidia_smi_memory()
        if mem is None:
            return 0
        used, total = mem
        free = total - used
        if free >= need_mib:
            return free
        log(f"GPU busy: {free} MiB free, need {need_mib} MiB; waiting {delay_s:.0f}s ({attempt + 1}/{retries})")
        time.sleep(delay_s)
    raise RuntimeError(f"GPU never had {need_mib} MiB free after {retries} retries; try again later.")


def processed_geometry(width: int, height: int, target: tuple[int, int] = TARGET_SIZE) -> tuple[float, int, int]:
    """Scale and crop offsets taking an original image to the model input (cover, then centre crop)."""
    tw, th = target
    scale = max(tw / width, th / height)
    rw, rh = math.ceil(width * scale), math.ceil(height * scale)
    return scale, (rw - tw) // 2, (rh - th) // 2


def open_reduced(path: Path, min_size: tuple[int, int]) -> Image.Image:
    """Open an image, letting the JPEG decoder skip detail beyond twice ``min_size``.

    Draft mode decodes a 24 MP photo at 1/2, 1/4 or 1/8 scale directly from the DCT, several
    times faster than decoding it whole and resizing. The aspect ratio is kept.
    """
    im = Image.open(path)
    if im.format == "JPEG":
        im.draft("RGB", (2 * min_size[0], 2 * min_size[1]))
    return im


def load_model_input(path: Path, target: tuple[int, int] = TARGET_SIZE, mirror: bool = False) -> Image.Image:
    with open_reduced(path, target) as im:
        im = im.convert("RGB")
        if mirror:
            im = im.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        scale, left, top = processed_geometry(im.width, im.height, target)
        rw, rh = math.ceil(im.width * scale), math.ceil(im.height * scale)
        im = im.resize((rw, rh), Image.Resampling.LANCZOS)
        return im.crop((left, top, left + target[0], top + target[1]))


def _cast(obj: Any, dtype) -> Any:
    """Cast floating tensors inside tensors, dataclasses, tuples, lists and dicts."""
    import dataclasses

    import torch

    if torch.is_tensor(obj):
        return obj.to(dtype) if obj.is_floating_point() else obj
    if dataclasses.is_dataclass(obj) and not isinstance(obj, type):
        changes = {f.name: _cast(getattr(obj, f.name), dtype) for f in dataclasses.fields(obj) if f.init}
        return dataclasses.replace(obj, **changes)
    if isinstance(obj, tuple) and hasattr(obj, "_fields"):
        return type(obj)(*(_cast(x, dtype) for x in obj))
    if isinstance(obj, (tuple, list)):
        return type(obj)(_cast(x, dtype) for x in obj)
    if isinstance(obj, dict):
        return {k: _cast(v, dtype) for k, v in obj.items()}
    return obj


def _run_in_half(module) -> None:
    """Store a submodule's weights in fp16 and cast at its boundary (fp16 in, fp32 out)."""
    import torch

    module.half()
    module.register_forward_pre_hook(lambda m, args, kwargs: (_cast(args, torch.float16), _cast(kwargs, torch.float16)),
                                     with_kwargs=True)
    module.register_forward_hook(lambda m, args, output: _cast(output, torch.float32))


def _without_cudnn(module) -> None:
    import torch

    def off(m, args):
        m._cudnn_was = torch.backends.cudnn.enabled
        torch.backends.cudnn.enabled = False

    def on(m, args, output):
        torch.backends.cudnn.enabled = getattr(m, "_cudnn_was", True)

    module.register_forward_pre_hook(off)
    module.register_forward_hook(on)


@contextlib.contextmanager
def pinned_torch_hub():
    """Route torch.hub loads of known repositories to a pinned commit of the official repository.

    The default would download whatever is on ``main`` today and run its ``hubconf.py``.
    ``skip_validation`` only skips torch's check that the ref is a branch or tag head; the
    download still comes from the official repository named in ``TORCH_HUB_PINS``.
    """
    import torch

    real = torch.hub.load

    def load(repo_or_dir, model, *args, **kwargs):
        pin = TORCH_HUB_PINS.get(repo_or_dir) if isinstance(repo_or_dir, str) else None
        if pin is not None:
            repo_or_dir = f"{repo_or_dir}:{pin['ref']}"
            kwargs["skip_validation"] = True
            kwargs["trust_repo"] = True
            kwargs["force_reload"] = False
        return real(repo_or_dir, model, *args, **kwargs)

    torch.hub.load = load
    try:
        yield
    finally:
        torch.hub.load = real


FULL_FRAME_DIAGONAL_MM = 43.2666


def exif_intrinsics(photo: dict[str, Any], target: tuple[int, int] = TARGET_SIZE) -> np.ndarray | None:
    """Pinhole intrinsics of the model input from the 35 mm-equivalent focal length in EXIF.

    Phone photos are distortion-corrected and the principal point is close to the centre, so the
    35 mm equivalent (defined on the frame diagonal) gives the focal length in pixels.
    """
    exif = photo.get("exif") or {}
    f35 = exif.get("focal_length_35mm")
    size = photo.get("image") or {}
    W, H = size.get("width"), size.get("height")
    if not f35 or not W or not H:
        return None
    f_px = f35 / FULL_FRAME_DIAGONAL_MM * math.hypot(W, H)
    scale, left, top = processed_geometry(W, H, target)
    return np.array([[f_px * scale, 0.0, W * scale / 2 - left],
                     [0.0, f_px * scale, H * scale / 2 - top],
                     [0.0, 0.0, 1.0]])


def batch_size_for(free_mib: int, *, headroom_mib: int = 1000, fixed_mib: int = 3800, per_view_mib: int = 85,
                   lo: int = 8, hi: int = 32) -> int:
    """Views per batch that fit in free VRAM (fp16 trunk).

    Measured on the RTX 2070 SUPER (8 GB), 6 October 2026: peak reserved 4472 MiB at 8 views,
    5826 at 24 and 6462 at 32 (about 3.8 GB fixed plus 83 MiB per view). 32 is the largest
    batch measured, so it is the ceiling.
    """
    n = (free_mib - headroom_mib - fixed_mib) // per_view_mib
    return int(max(lo, min(hi, n)))


class MapAnythingBackend:
    """MapAnything in fp16 weights with memory-efficient inference, sized for an 8 GB card."""

    name = "mapanything"

    def __init__(self, model_id: str = POSE_MODEL, *, half: bool = True, head_minibatch: int = 2, log=print):
        import torch

        self.torch = torch
        self.head_minibatch = head_minibatch
        self.model_id = model_id
        self.half = half
        self.log = log
        self.model = None
        self.stats = GpuStats()
        self.weights_mib = 0.0

    def load(self) -> None:
        from mapanything.models import MapAnything

        info = MODEL_REGISTRY[self.model_id]
        t0 = time.perf_counter()
        with pinned_torch_hub():
            model = MapAnything.from_pretrained(self.model_id, revision=info["revision"])
        if self.half:
            # The image encoder and the multi-view transformer hold ~92% of the weights and run
            # under autocast; the small geometric encoders and heads stay fp32 because parts of them
            # run outside autocast and refuse mixed dtypes.
            for name in HALF_MODULES:
                _run_in_half(getattr(model, name))
        for name in NO_CUDNN_MODULES:
            _without_cudnn(getattr(model, name))
        self.model = model.to("cuda").eval()
        self.torch.cuda.synchronize()
        self.weights_mib = self.torch.cuda.memory_allocated() / 2**20
        self.log(f"Loaded {self.model_id} ({self.weights_mib:.0f} MiB on GPU) in {time.perf_counter() - t0:.1f}s")

    def unload(self) -> None:
        self.model = None
        self.torch.cuda.empty_cache()

    def _views(self, paths: list[Path], intrinsics: list[np.ndarray | None] | None = None,
               mirror: list[bool] | None = None) -> list[dict[str, Any]]:
        import torchvision.transforms as tvf
        from uniception.models.encoders.image_normalizations import IMAGE_NORMALIZATION_DICT

        norm = IMAGE_NORMALIZATION_DICT["dinov2"]
        to_tensor = tvf.Compose([tvf.ToTensor(), tvf.Normalize(mean=norm.mean, std=norm.std)])
        views = []
        for i, path in enumerate(paths):
            img = load_model_input(path, mirror=bool(mirror and mirror[i]))
            view = {
                "img": to_tensor(img)[None],
                "true_shape": np.int32([img.size[::-1]]),
                "idx": i,
                "instance": str(i),
                "data_norm_type": ["dinov2"],
            }
            if intrinsics is not None and intrinsics[i] is not None:
                import torch

                view["intrinsics"] = torch.from_numpy(np.asarray(intrinsics[i], np.float32))[None]
            views.append(view)
        return views

    def predict(self, paths: list[Path], intrinsics: list[np.ndarray | None] | None = None,
                mirror: list[bool] | None = None) -> list[dict[str, np.ndarray]]:
        """Poses, intrinsics and point maps for one batch, all in the batch's own metric frame."""
        torch = self.torch
        if self.model is None:
            self.load()
        t_prep = time.perf_counter()
        views = self._views(paths, intrinsics, mirror)
        self.stats.prepare_seconds += time.perf_counter() - t_prep
        torch.cuda.reset_peak_memory_stats()
        torch.cuda.synchronize()
        t0 = time.perf_counter()
        with torch.no_grad():
            preds = self.model.infer(
                views,
                memory_efficient_inference=True,
                # A fixed, small head minibatch: the adaptive default takes 95% of free VRAM,
                # which spills into shared memory on Windows and crowds other GPU users.
                minibatch_size=self.head_minibatch,
                use_amp=True,
                amp_dtype="fp16",  # the RTX 20xx has no bf16
                apply_mask=True,
                mask_edges=True,
            )
        torch.cuda.synchronize()
        seconds = time.perf_counter() - t0
        peak_a = torch.cuda.max_memory_allocated() / 2**20
        peak_r = torch.cuda.max_memory_reserved() / 2**20
        self.stats.seconds += seconds
        self.stats.peak_allocated_mib = max(self.stats.peak_allocated_mib, peak_a)
        self.stats.peak_reserved_mib = max(self.stats.peak_reserved_mib, peak_r)
        self.stats.batches.append({"views": len(paths), "seconds": round(seconds, 2),
                                   "peak_allocated_mib": round(peak_a), "peak_reserved_mib": round(peak_r)})
        s = STORE_STRIDE
        out = []
        for pred in preds:
            pts = pred["pts3d_cam"][0].float().cpu().numpy()
            conf = pred["conf"][0].float().cpu().numpy()
            mask = pred["mask"][0, ..., 0].bool().cpu().numpy() if pred["mask"].ndim == 4 else pred["mask"][0].bool().cpu().numpy()
            K_model = pred["intrinsics"][0].float().cpu().numpy().astype(np.float64)
            K_store = K_model.copy()
            K_store[:2] /= s  # intrinsics of the stored, subsampled point map
            out.append({
                "c2w": pred["camera_poses"][0].float().cpu().numpy().astype(np.float64),
                "K": K_store,
                "K_model": K_model,
                "pts_cam": pts[::s, ::s].astype(np.float16),
                "conf": conf[::s, ::s].astype(np.float16),
                "mask": mask[::s, ::s],
            })
        del preds
        torch.cuda.empty_cache()
        return out
