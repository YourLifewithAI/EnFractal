import argparse
import json
from .exporter import export_room


def main():
    parser = argparse.ArgumentParser(description='Export a validated landscape package as a game room.')
    for name in ('package', 'room', 'room-id', 'out'):
        parser.add_argument('--'+name, required=True)
    args = parser.parse_args()
    try:
        stats = export_room(args.package, args.room, args.room_id, args.out)
    except (ValueError, OSError, KeyError, TypeError) as error:
        parser.exit(1, 'EXPORT_INVALID '+str(error)+'\n')
    print('ROOM_EXPORTED '+json.dumps(stats, sort_keys=True))


if __name__ == '__main__':
    main()
