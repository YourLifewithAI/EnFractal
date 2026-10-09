"""Reading a drawing: the photo's ink, the interpretation's seeds and boxes, and the shapes they pick out.

Runs under plain Python with numpy and Pillow (no scipy). Everything here is deterministic: the same photo and
interpretation give the same shapes. The photo's pixels never leave this module: what comes out is masks turned
into balls (centre, radius) and stroke centrelines, in metres.

Coordinates in an interpretation are fractions of the photo's width and height, origin at the top left.
"""
import math

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageOps

WORK = 1200          # long side of the working crop, pixels
MAX_CLOSE = 7        # widest pencil gap we bridge, in working pixels
# A pixel darker than this fraction of the local paper is a drawn line. Firm pen first; light pencil if the firm
# reading leaves the areas open (the light level also counts pencil shading as line, so it comes second).
LINE_LEVELS = (0.5, 0.8, 0.9)


# ---------------------------------------------------------------- the photo

class Sheet:
    """The figure's crop of the photo, normalised against the paper (shadows and uneven light removed)."""

    def __init__(self, photo_path, figure_box):
        image = ImageOps.exif_transpose(Image.open(photo_path)).convert('L')
        self.photo_w, self.photo_h = image.size
        x0, y0, x1, y1 = figure_box
        margin = 0.03
        self.box_px = (max(0, int((x0 - margin) * self.photo_w)), max(0, int((y0 - margin) * self.photo_h)),
                       min(self.photo_w, int(math.ceil((x1 + margin) * self.photo_w))),
                       min(self.photo_h, int(math.ceil((y1 + margin) * self.photo_h))))
        crop = image.crop(self.box_px)
        self.scale = WORK / max(crop.size)
        size = (max(8, round(crop.width * self.scale)), max(8, round(crop.height * self.scale)))
        crop = crop.resize(size, Image.LANCZOS)
        self.w, self.h = crop.size
        paper = crop.filter(ImageFilter.MaxFilter(21)).filter(ImageFilter.GaussianBlur(25))
        ratio = np.asarray(crop, np.float64) / np.maximum(np.asarray(paper, np.float64), 1.0)
        self.ratio = ratio
        self._ink = {}
        self.ink = self.ink_at(0)
        # Only the inside of the figure box counts; the margin keeps flood fills from leaking around the edge.
        self.inside = np.zeros(ratio.shape, bool)
        fx0, fy0 = self.to_px((x0, y0))
        fx1, fy1 = self.to_px((x1, y1))
        self.inside[max(0, fy0):min(self.h, fy1 + 1), max(0, fx0):min(self.w, fx1 + 1)] = True
        self._walls = {}
        self.rim = self.inside & ~_erode(self.inside, 1)

    def ink_at(self, level):
        if level not in self._ink:
            t = LINE_LEVELS[level]
            ink = self.ratio < t
            # Drop speckle (paper grain), keep every real stroke.
            ink = _open(ink, 1) | (ink & (self.ratio < t * 0.6))
            self._ink[level] = ink
        return self._ink[level]

    def silhouette(self, close=3):
        """Everything the figure's outer lines enclose (the lines included): firm pen first, fainter pencil if the
        firm lines leave the figure open."""
        if 'silhouette' not in self._walls:
            seed = np.zeros_like(self.inside)
            seed[0, :] = seed[-1, :] = seed[:, 0] = seed[:, -1] = True
            best = None
            for level in range(len(LINE_LEVELS)):
                free = ~self.walls(close, level)
                outside = grow(free | ~self.inside, seed)
                shape = ~_dilate(outside, close) & self.inside
                best = shape
                if shape.sum() < 0.85 * self.inside.sum() and (~shape & self.inside).sum() > 0:
                    # Closed enough: the paper around the figure was reached from the edge.
                    if shape.sum() > 0.02 * self.inside.sum():
                        break
            self._walls['silhouette'] = best
        return self._walls['silhouette']

    def walls(self, close, level=0):
        if (close, level) not in self._walls:
            ink = self.ink_at(level)
            self._walls[(close, level)] = (_dilate(ink, close) if close else ink) | ~self.inside
        return self._walls[(close, level)]

    def to_px(self, point):
        """Photo fractions to working pixels (x, y), rounded."""
        x = (point[0] * self.photo_w - self.box_px[0]) * self.scale
        y = (point[1] * self.photo_h - self.box_px[1]) * self.scale
        return int(round(x)), int(round(y))

    def to_px_f(self, point):
        return ((point[0] * self.photo_w - self.box_px[0]) * self.scale,
                (point[1] * self.photo_h - self.box_px[1]) * self.scale)

    def box_mask(self, box):
        x0, y0 = self.to_px(box[:2])
        x1, y1 = self.to_px(box[2:])
        m = np.zeros((self.h, self.w), bool)
        m[max(0, min(y0, y1)):max(0, max(y0, y1)) + 1, max(0, min(x0, x1)):max(0, max(x0, x1)) + 1] = True
        return m

    def polygon_mask(self, points):
        img = Image.new('L', (self.w, self.h), 0)
        ImageDraw.Draw(img).polygon([self.to_px_f(p) for p in points], fill=255)
        return np.asarray(img) > 0

    def ellipse_mask(self, ellipse):
        cx, cy = self.to_px_f(ellipse[:2])
        rx = ellipse[2] * self.photo_w * self.scale
        ry = ellipse[3] * self.photo_h * self.scale
        img = Image.new('L', (self.w, self.h), 0)
        ImageDraw.Draw(img).ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=255)
        return np.asarray(img) > 0


# ---------------------------------------------------------------- morphology (numpy only)

def _shift_stack(mask, offsets, fill=False):
    h, w = mask.shape
    pad = max(max(abs(dx), abs(dy)) for dx, dy in offsets)
    p = np.pad(mask, pad, constant_values=fill)
    return [p[pad + dy:pad + dy + h, pad + dx:pad + dx + w] for dx, dy in offsets]


_CROSS = [(0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)]
_SQUARE = [(dx, dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1)]


def _dilate(mask, steps):
    for i in range(steps):
        mask = np.logical_or.reduce(_shift_stack(mask, _CROSS if i % 2 == 0 else _SQUARE))
    return mask


def _erode(mask, steps):
    for i in range(steps):
        mask = np.logical_and.reduce(_shift_stack(mask, _CROSS if i % 2 == 0 else _SQUARE))
    return mask


def _open(mask, steps):
    return _dilate(_erode(mask, steps), steps)


def _close(mask, steps):
    return _erode(_dilate(mask, steps), steps)


def distance(mask, limit=2000, edge_is_outside=True):
    """Approximate Euclidean distance to the outside, in pixels (octagonal erosion)."""
    d = np.zeros(mask.shape, np.float64)
    cur = mask.copy()
    i = 0
    while cur.any() and i < limit:
        d += cur
        cur = np.logical_and.reduce(_shift_stack(cur, _CROSS if i % 2 == 0 else _SQUARE, not edge_is_outside))
        i += 1
    # Square/cross alternation gives octagonal levels; scale to Euclidean on average.
    return d * 0.96


def _runs(free):
    """Label horizontal runs of free pixels; 0 where not free."""
    h, w = free.shape
    starts = free.copy()
    starts[:, 1:] &= ~free[:, :-1]
    ids = np.cumsum(starts.ravel()).reshape(h, w)
    return np.where(free, ids, 0)


def grow(free, marked):
    """Everything in free connected (4-neighbours) to marked: whole runs spread along rows, then columns, until
    nothing changes. Vectorised, so a big area costs a few passes, not a pixel-by-pixel walk."""
    marked = marked & free
    rows = _runs(free)
    cols = _runs(free.T.copy())
    while True:
        flag = np.zeros(rows.max() + 1, bool)
        flag[rows[marked]] = True
        flag[0] = False
        new = flag[rows]
        flag = np.zeros(cols.max() + 1, bool)
        flag[cols[new.T]] = True
        flag[0] = False
        new = flag[cols].T
        if (new == marked).all():
            return new
        marked = new


def fill_holes(mask):
    padded = np.pad(mask, 1)
    seed = np.zeros_like(padded)
    seed[0, :] = seed[-1, :] = True
    seed[:, 0] = seed[:, -1] = True
    outside = grow(~padded, seed & ~padded)
    return ~outside[1:-1, 1:-1]


def flood(walls, seed):
    """The connected non-wall area containing seed (x, y)."""
    m = np.zeros(walls.shape, bool)
    m[seed[1], seed[0]] = True
    return grow(~walls, m)


def component(mask, seed):
    """The connected area of mask containing seed (x, y)."""
    m = np.zeros(mask.shape, bool)
    m[seed[1], seed[0]] = True
    return grow(mask, m)


def _nearest_free(walls, seed, radius):
    x, y = seed
    h, w = walls.shape
    if 0 <= x < w and 0 <= y < h and not walls[y, x]:
        return seed
    for r in range(1, radius + 1):
        best = None
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if max(abs(dx), abs(dy)) != r:
                    continue
                xx, yy = x + dx, y + dy
                if 0 <= xx < w and 0 <= yy < h and not walls[yy, xx]:
                    d = dx * dx + dy * dy
                    if best is None or d < best[0]:
                        best = (d, (xx, yy))
        if best:
            return best[1]
    return None


# ---------------------------------------------------------------- what the interpretation points at

def region(sheet, spec, notes, max_fraction=0.45, max_area=None):
    """An enclosed area of the drawing, from seeds; the outline or ellipse cuts leaks or stands in for missing lines.
    An area that runs out to the figure's box, or grows past max_fraction of it (or past max_area pixels), leaked
    through a gap in the pencil: the next try bridges wider gaps or counts fainter lines."""
    total = _region(sheet, spec, notes, max_fraction, max_area)
    for s in spec.get('minus_seeds', []):
        total &= ~_region(sheet, {'seeds': [s], 'name': spec.get('name', '?') + ' (minus)',
                                  'others': spec.get('seeds', [])}, notes, 0.45)
    return total


def _region(sheet, spec, notes, max_fraction, max_area=None):
    hint = None
    if spec.get('outline'):
        hint = sheet.polygon_mask(spec['outline'])
    elif spec.get('ellipse'):
        hint = sheet.ellipse_mask(spec['ellipse'])
    if spec.get('silhouette'):
        if hint is None:
            notes.append(f"{spec.get('name', '?')}: 'silhouette' needs an outline; skipped")
            return np.zeros((sheet.h, sheet.w), bool)
        return sheet.silhouette() & hint
    seeds = [sheet.to_px(s) for s in spec.get('seeds', [])]
    figure_area = sheet.inside.sum()
    total = np.zeros((sheet.h, sheet.w), bool)
    others = [sheet.to_px(s) for s in spec.get('others', [])]
    for seed in seeds:
        found = relaxed = None
        tries = [(level, close) for level in range(len(LINE_LEVELS)) for close in range(0, MAX_CLOSE + 1)]
        for level, close in tries:
            walls = sheet.walls(close, level)
            start = _nearest_free(walls, seed, 6 + close * 2)
            if start is None:
                continue
            area = flood(walls, start)
            touches = (area & sheet.rim).any()
            leaked = touches or area.sum() > max_fraction * figure_area
            if max_area is not None and area.sum() > max_area:
                leaked = True
            if hint is not None and area.sum() > 3.0 * max(hint.sum(), 1):
                leaked = True
            if not leaked and area.sum() >= 12:
                # An area that swallows another piece's seed ran through a gap in the line between them.
                if any(0 <= y < sheet.h and 0 <= x < sheet.w and area[y, x] for x, y in others):
                    if relaxed is None:
                        relaxed = (area, close)
                    continue
                # Back out to the middle of the line, so neighbouring areas meet.
                found = _dilate(area, close + 1)
                break
        if found is None and relaxed is not None:
            notes.append(f"{spec.get('name', '?')}: seed {seed} shares its area with another piece")
            found = _dilate(relaxed[0], relaxed[1] + 1)
        if found is None:
            if hint is not None:
                notes.append(f"{spec.get('name', '?')}: seed {seed} leaked; used the outline")
                found = hint.copy()
            else:
                notes.append(f"{spec.get('name', '?')}: seed {seed} leaked and no outline; skipped")
                continue
        total |= found
    if not total.any() and hint is not None:
        total = hint.copy()
    if not total.any():
        return total
    total = fill_holes(total)
    # Pencil shading leaves bites out of an area; close them without crossing lines.
    total = fill_holes(_close(total, 4)) & (_dilate(total, 4))
    if spec.get('clip'):
        total &= sheet.box_mask(spec['clip'])
    return total


def blob(sheet, spec, notes):
    """A solid inked area (a filled pupil, a nose): the ink at the seed with thin lines peeled off."""
    out = np.zeros((sheet.h, sheet.w), bool)
    solid = _open(sheet.ink, 2)
    for s in spec.get('seeds', []):
        seed = sheet.to_px(s)
        start = _nearest_free(~solid, seed, 8)
        if start is None:
            continue
        comp = component(solid, start)
        if comp.sum() > 0.05 * sheet.inside.sum():
            notes.append(f"{spec.get('name', '?')}: blob too big, ignored")
            continue
        out |= _dilate(comp, 1)
    if not out.any():
        return region(sheet, spec, notes)
    out = fill_holes(out)
    if spec.get('clip'):
        out &= sheet.box_mask(spec['clip'])
    return out


def stroke(sheet, spec, notes):
    """Drawn lines inside a box (a mouth, brows, blush marks, a tail drawn as one line): their centreline pixels
    and their drawn half-width, in working pixels."""
    if spec.get('points'):
        pts = [sheet.to_px_f(p) for p in spec['points']]
        line = []
        for (ax, ay), (bx, by) in zip(pts, pts[1:]):
            n = max(1, int(math.hypot(bx - ax, by - ay)))
            for i in range(n):
                t = i / n
                line.append((ax + (bx - ax) * t, ay + (by - ay) * t))
        line.append(pts[-1])
        return np.array(line, np.float64), 1.5
    if not spec.get('box'):
        notes.append(f"{spec.get('name', '?')}: a line needs a box or points; skipped")
        return np.zeros((0, 2)), 1.0
    box = sheet.box_mask(spec['box'])
    for level in range(len(LINE_LEVELS)):
        ink = sheet.ink_at(level) & sheet.inside
        keep = np.zeros_like(ink)
        seen = np.zeros_like(ink)
        ys, xs = np.nonzero(ink & box)
        for y, x in zip(ys, xs):
            if seen[y, x]:
                continue
            comp = component(ink, (int(x), int(y)))
            seen |= comp
            if comp.sum() < 10 and not spec.get('keep_solid'):
                continue  # a speck of graphite or paper grain, not a line
            inside = (comp & box).sum()
            if inside >= 0.6 * comp.sum():
                keep |= comp
            elif inside >= 6 and spec.get('take_all'):
                # A long line passing through the box (an outline the stroke touches): take the part in the box.
                keep |= comp & box
        if not spec.get('keep_solid'):
            # Solid inked areas (a filled pupil) are stickers of their own, not lines: peel them off.
            keep &= ~_dilate(_open(keep, 5), 2)
        if keep.sum() >= 10:
            break
    if not keep.any():
        notes.append(f"{spec.get('name', '?')}: no lines found in its box")
        return np.zeros((0, 2)), 1.0
    ky, kx = np.nonzero(keep)
    y0, x0 = max(0, ky.min() - 2), max(0, kx.min() - 2)
    skel = np.zeros_like(keep)
    skel[y0:ky.max() + 3, x0:kx.max() + 3] = skeleton(_close(keep[y0:ky.max() + 3, x0:kx.max() + 3], 2))
    ys, xs = np.nonzero(skel)
    half = max(1.0, keep.sum() / max(1, len(xs)) / 2.0)
    order = np.lexsort((xs, ys))
    return np.stack([xs[order], ys[order]], axis=1).astype(np.float64), half


def skeleton(mask):
    """Zhang-Suen thinning."""
    m = np.pad(mask.astype(bool), 1)
    while True:
        changed = False
        for step in (0, 1):
            p = m
            n = [np.roll(np.roll(p, -dy, 0), -dx, 1) for dx, dy in
                 [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)]]
            b = sum(x.astype(np.int32) for x in n)
            a = sum(((~n[i]) & n[(i + 1) % 8]).astype(np.int32) for i in range(8))
            if step == 0:
                c = ~(n[0] & n[2] & n[4]) & ~(n[2] & n[4] & n[6])
            else:
                c = ~(n[0] & n[2] & n[6]) & ~(n[0] & n[4] & n[6])
            remove = p & (b >= 2) & (b <= 6) & (a == 1) & c
            if remove.any():
                m = p & ~remove
                changed = True
        if not changed:
            break
    return m[1:-1, 1:-1]


# ---------------------------------------------------------------- shapes to balls

def mask_balls(mask, max_balls=2600):
    """A filled shape as a union of inscribed discs: (x, y, r) in working pixels. Inflating each disc to a ball
    gives the drawing's silhouette from the front and a rounded body from every other side."""
    if not mask.any():
        return np.zeros((0, 3)), 0.0
    ys, xs = np.nonzero(mask)
    y0, x0 = ys.min(), xs.min()
    sub = mask[y0:ys.max() + 1, x0:xs.max() + 1]
    d = np.zeros(mask.shape)
    d[y0:ys.max() + 1, x0:xs.max() + 1] = distance(sub)
    area = mask.sum()
    stride = max(1, int(math.ceil(math.sqrt(area / max_balls))))
    ys, xs = np.nonzero(mask)
    sel = (ys % stride == 0) & (xs % stride == 0)
    ys, xs = ys[sel], xs[sel]
    r = d[ys, xs]
    keep = r >= max(0.75, stride * 0.5)
    balls = np.stack([xs[keep], ys[keep], r[keep]], axis=1).astype(np.float64)
    # The deepest discs first, so a renderer that caps the count keeps the body.
    order = np.lexsort((balls[:, 0], balls[:, 1], -balls[:, 2]))
    return balls[order], float(d.max())


def bands(mask, n, inner=None, solid=None):
    """Split an area into n stripes that follow its shape, outermost first: by distance from the inner edge (the
    hole it wraps around, like a rainbow's sky) against distance from the outer edge. solid marks neighbours that
    do not count as an edge (the cloud a rainbow stands in)."""
    if n <= 1 or not mask.any():
        return [mask]
    ys, xs = np.nonzero(mask)
    pad = 4
    y0, y1 = max(0, ys.min() - pad), ys.max() + pad + 1
    x0, x1 = max(0, xs.min() - pad), xs.max() + pad + 1
    m = mask[y0:y1, x0:x1]
    body = m | (solid[y0:y1, x0:x1] if solid is not None else False)
    if inner is not None:
        body = body | inner[y0:y1, x0:x1]
    d_out = distance(body)
    if inner is not None and inner[y0:y1, x0:x1].any():
        d_in = distance(~inner[y0:y1, x0:x1], limit=int(d_out.max() * 4) + 8, edge_is_outside=False)
        t = d_out / np.maximum(d_out + d_in, 1e-6)
    else:
        t = d_out / max(d_out[m].max(), 1e-6)
    k = np.clip((t * n).astype(int), 0, n - 1)
    out = []
    for i in range(n):
        band = np.zeros_like(mask)
        band[y0:y1, x0:x1] = m & (k == i)
        out.append(band)
    return out
