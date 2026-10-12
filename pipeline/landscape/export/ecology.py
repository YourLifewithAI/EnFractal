"""Authored plant/stock-part identities, independent of display names and scene nodes."""
COMBUSTIBLE = {'foliage', 'bark', 'timber', 'wood', 'cloth'}
PLANTS = {'broadleaf', 'conifer', 'shrub', 'grass_tuft', 'flower_clump', 'fern'}


def tag(parts, plant_id, prototype, records, position):
    species = prototype if prototype in PLANTS else (prototype if any(p['role'] == 'foliage' for p in parts) else '')
    # Flower heads reuse snow/gravel palette roles. They still belong to the plant:
    # retain their geometry/material identity for burning, growth and seed propagation.
    roles = COMBUSTIBLE | ({'snow', 'gravel'} if species else set())
    for role in sorted({p['role'] for p in parts} & roles):
        group = [p for p in parts if p['role'] == role]
        identity = f'{plant_id}_{role}'
        vertices = [v for p in group for v in p['positions']]
        records.append(dict(id=identity, group=plant_id, species=species, role=role,
                            position_m=list(position),
                            min_m=[min(v[i] for v in vertices) for i in range(3)],
                            max_m=[max(v[i] for v in vertices) for i in range(3)]))
        for p in group:
            p['ecology_id'] = identity


def bind(parts, target, records):
    ids = {p.get('ecology_id') for p in parts}
    for record in records:
        if record['id'] in ids:
            record['target'] = target
