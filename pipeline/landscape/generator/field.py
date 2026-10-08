"""Seeded noise, a regular height grid and small geometric helpers (standard library)."""
import math
import random


def clamp(v, lo=0., hi=1.):
    return lo if v < lo else hi if v > hi else v


def smooth(t):
    t = clamp(t)
    return t*t*(3-2*t)


def lerp(a, b, t):
    return a+(b-a)*t


def smax(a, b, k):
    """Polynomial smooth maximum: neighbouring landforms merge into saddles."""
    h = max(k-abs(a-b), 0.)/k
    return max(a, b)+h*h*k*.25


def bell(t):
    """1 at t=0, 0 at t>=1; convex crest and concave foot, like a weathered hill."""
    if t >= 1:
        return 0.
    return .5+.5*math.cos(math.pi*max(0., t))


def rr_dist(lx, lz, a, b, r):
    """Signed distance to a rounded rectangle of half extents a, b."""
    r = min(r, a, b)
    qx = abs(lx)-a+r
    qz = abs(lz)-b+r
    return math.hypot(max(qx, 0.), max(qz, 0.))+min(max(qx, qz), 0.)-r


def seg_dist(px, pz, ax, az, bx, bz):
    dx, dz = bx-ax, bz-az
    L = dx*dx+dz*dz
    t = 0. if L == 0 else clamp(((px-ax)*dx+(pz-az)*dz)/L)
    return math.hypot(px-ax-t*dx, pz-az-t*dz), t


class Noise:
    """Value noise on a seeded lattice; deterministic for a seed."""

    def __init__(self, seed, size=128):
        rnd = random.Random(seed)
        self.n = size
        self.t = [rnd.random()*2-1 for _ in range(size*size)]

    def __call__(self, x, z):
        n = self.n
        xf = math.floor(x)
        zf = math.floor(z)
        fx = x-xf
        fz = z-zf
        i = int(xf) % n
        j = int(zf) % n
        i1 = (i+1) % n
        j1 = (j+1) % n
        t = self.t
        a = t[j*n+i]
        b = t[j*n+i1]
        c = t[j1*n+i]
        d = t[j1*n+i1]
        ux = fx*fx*(3-2*fx)
        uz = fz*fz*(3-2*fz)
        return a+(b-a)*ux+(c-a)*uz+(a-b-c+d)*ux*uz

    def fbm(self, x, z, octaves=4, gain=.5):
        s = 0.
        amp = 1.
        norm = 0.
        f = 1.
        for o in range(octaves):
            s += amp*self(x*f+o*17.31, z*f-o*11.17)
            norm += amp
            amp *= gain
            f *= 2.03
        return s/norm

    def ridged(self, x, z, octaves=3):
        s = 0.
        amp = 1.
        norm = 0.
        f = 1.
        for o in range(octaves):
            s += amp*(1-abs(self(x*f-o*7.7, z*f+o*3.9)))
            norm += amp
            amp *= .5
            f *= 2.1
        return s/norm


class Grid:
    """Vertices at x0+i*cell, z0+j*cell; flat lists indexed j*nx+i."""

    def __init__(self, x0, z0, x1, z1, cell):
        self.cell = cell
        self.nx = int(round((x1-x0)/cell))+1
        self.nz = int(round((z1-z0)/cell))+1
        self.x0 = x0
        self.z0 = z0
        self.xs = [round(x0+i*cell, 6) for i in range(self.nx)]
        self.zs = [round(z0+j*cell, 6) for j in range(self.nz)]
        self.n = self.nx*self.nz

    def zeros(self, value=0.):
        return [value]*self.n

    def span(self, x0, x1, z0, z1):
        c = self.cell
        i0 = max(0, int(math.floor((x0-self.x0)/c)))
        i1 = min(self.nx-1, int(math.ceil((x1-self.x0)/c)))
        j0 = max(0, int(math.floor((z0-self.z0)/c)))
        j1 = min(self.nz-1, int(math.ceil((z1-self.z0)/c)))
        return range(i0, i1+1), range(j0, j1+1)

    def nearest(self, x, z):
        i = min(self.nx-1, max(0, int(round((x-self.x0)/self.cell))))
        j = min(self.nz-1, max(0, int(round((z-self.z0)/self.cell))))
        return i, j

    def sample(self, f, x, z):
        fx = clamp((x-self.x0)/self.cell, 0, self.nx-1.000001)
        fz = clamp((z-self.z0)/self.cell, 0, self.nz-1.000001)
        i = int(fx)
        j = int(fz)
        u = fx-i
        v = fz-j
        nx = self.nx
        a = f[j*nx+i]
        b = f[j*nx+i+1]
        c = f[(j+1)*nx+i]
        d = f[(j+1)*nx+i+1]
        return (a*(1-u)+b*u)*(1-v)+(c*(1-u)+d*u)*v

    def tri_height(self, f, x, z):
        """Height on the actual triangulated surface (checkerboard diagonals)."""
        fx = clamp((x-self.x0)/self.cell, 0, self.nx-1.000001)
        fz = clamp((z-self.z0)/self.cell, 0, self.nz-1.000001)
        i = int(fx)
        j = int(fz)
        u = fx-i
        v = fz-j
        nx = self.nx
        a = f[j*nx+i]
        b = f[j*nx+i+1]
        c = f[(j+1)*nx+i]
        d = f[(j+1)*nx+i+1]
        if (i+j) % 2 == 0:   # diagonal a-d
            return a+(d-c)*u+(c-a)*v if v > u else a+(b-a)*u+(d-b)*v
        # diagonal b-c
        return a+(b-a)*u+(c-a)*v if u+v < 1 else d+(c-d)*(1-u)+(b-d)*(1-v)

    def triangles(self, keep=None):
        """Checkerboard-diagonal triangles, counter-clockwise seen from +Y."""
        nx = self.nx
        out = []
        for j in range(self.nz-1):
            for i in range(nx-1):
                if keep is not None and not keep(i, j):
                    continue
                a = j*nx+i
                b = a+1
                c = a+nx
                d = c+1
                if (i+j) % 2 == 0:
                    out.append((a, c, d))
                    out.append((a, d, b))
                else:
                    out.append((a, c, b))
                    out.append((b, c, d))
        return out


def slope_field(grid, h):
    """Per-vertex steepest incident gradient in degrees (central differences)."""
    nx, nz, c = grid.nx, grid.nz, grid.cell
    out = [0.]*grid.n
    for j in range(nz):
        jm = max(0, j-1)
        jp = min(nz-1, j+1)
        for i in range(nx):
            im = max(0, i-1)
            ip = min(nx-1, i+1)
            gx = (h[j*nx+ip]-h[j*nx+im])/((ip-im)*c)
            gz = (h[jp*nx+i]-h[jm*nx+i])/((jp-jm)*c)
            out[j*nx+i] = math.degrees(math.atan(math.hypot(gx, gz)))
    return out


def face_slope_max(grid, h, tris):
    """Per-vertex maximum slope of incident triangle faces, degrees."""
    out = [0.]*grid.n
    nx = grid.nx
    xs, zs = grid.xs, grid.zs
    for t in tris:
        p = [(xs[k % nx], h[k], zs[k//nx]) for k in t]
        ux, uy, uz = p[1][0]-p[0][0], p[1][1]-p[0][1], p[1][2]-p[0][2]
        vx, vy, vz = p[2][0]-p[0][0], p[2][1]-p[0][1], p[2][2]-p[0][2]
        nxx = uy*vz-uz*vy
        nyy = uz*vx-ux*vz
        nzz = ux*vy-uy*vx
        length = math.sqrt(nxx*nxx+nyy*nyy+nzz*nzz)
        s = math.degrees(math.acos(clamp(abs(nyy)/length, 0, 1)))
        for k in t:
            if s > out[k]:
                out[k] = s
    return out


def blur(grid, f, passes=1, mask=None):
    nx, nz = grid.nx, grid.nz
    for _ in range(passes):
        g = f[:]
        for j in range(1, nz-1):
            row = j*nx
            for i in range(1, nx-1):
                k = row+i
                if mask is not None and mask[k] <= 0:
                    continue
                avg = (f[k-1]+f[k+1]+f[k-nx]+f[k+nx])*.125+f[k]*.5
                g[k] = avg if mask is None else f[k]+(avg-f[k])*mask[k]
        f = g
    return f


def distance_field(grid, seeds):
    """Two-pass chamfer distance (metres) to the nearest seed vertex."""
    big = 1e9
    c = grid.cell
    dg = c*math.sqrt(2)
    nx, nz = grid.nx, grid.nz
    d = [0. if s else big for s in seeds]
    for j in range(nz):
        for i in range(nx):
            q = j*nx+i
            v = d[q]
            if i > 0 and d[q-1]+c < v:
                v = d[q-1]+c
            if j > 0:
                if d[q-nx]+c < v:
                    v = d[q-nx]+c
                if i > 0 and d[q-nx-1]+dg < v:
                    v = d[q-nx-1]+dg
                if i < nx-1 and d[q-nx+1]+dg < v:
                    v = d[q-nx+1]+dg
            d[q] = v
    for j in range(nz-1, -1, -1):
        for i in range(nx-1, -1, -1):
            q = j*nx+i
            v = d[q]
            if i < nx-1 and d[q+1]+c < v:
                v = d[q+1]+c
            if j < nz-1:
                if d[q+nx]+c < v:
                    v = d[q+nx]+c
                if i < nx-1 and d[q+nx+1]+dg < v:
                    v = d[q+nx+1]+dg
                if i > 0 and d[q+nx-1]+dg < v:
                    v = d[q+nx-1]+dg
            d[q] = v
    return d
