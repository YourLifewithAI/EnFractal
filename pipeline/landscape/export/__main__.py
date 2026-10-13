import argparse
import json
from .exporter import export_room


def main():
    parser = argparse.ArgumentParser(description='Export a validated landscape package as a game room.')
    for name in ('package', 'room', 'room-id', 'out'):
        parser.add_argument('--'+name, required=True)
    # The scale experiment (generator/scale.py): a room magnified x W.
    parser.add_argument('--world-scale', type=float, default=1.)
    args = parser.parse_args()
    if args.world_scale != 1:
        from pipeline.landscape.harness.package import allow_world_scale
        from . import source
        allow_world_scale(args.world_scale)
        source.OPENING_MAX_M = 20*args.world_scale
        from . import exporter
        exporter.WORLD_SCALE = args.world_scale
    try:
        stats = export_room(args.package, args.room, args.room_id, args.out)
    except (ValueError, OSError, KeyError, TypeError) as error:
        parser.exit(1, 'EXPORT_INVALID '+str(error)+'\n')
    print('ROOM_EXPORTED '+json.dumps(stats, sort_keys=True))


if __name__ == '__main__':
    main()
