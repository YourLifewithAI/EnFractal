"""Real PostgreSQL + service restart + actual Godot map; isolated disposable DB."""
from pathlib import Path
import argparse
import json
import os
import secrets
import socket
import subprocess
import sys
import time
import psycopg
from psycopg import sql
from psycopg.conninfo import conninfo_to_dict, make_conninfo

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--capture', action='store_true')
    args = parser.parse_args()
    config = json.loads((ROOT/'.cache/save-travel-config.json').read_text(encoding='utf-8-sig'))
    database = f'enfractal_integration_{os.getpid()}_{secrets.token_hex(3)}'
    connection_args = conninfo_to_dict(config['database_url'])
    admin_args = {**connection_args, 'dbname': 'postgres'}
    with psycopg.connect(**admin_args, autocommit=True) as connection:
        connection.execute(sql.SQL('CREATE DATABASE {}').format(sql.Identifier(database)))
    sandbox = ROOT/'.cache'/database
    sandbox.mkdir()
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', 0))
        port = probe.getsockname()[1]
    local_config = {**config, 'database_url':make_conninfo(**{**connection_args,'dbname':database}), 'token':secrets.token_hex(32)}
    config_path = sandbox/'config.json'
    config_path.write_text(json.dumps(local_config), encoding='utf-8')
    engine = Path(os.environ.get('ENFRACTAL_GODOT', ROOT/'.cache/godot/Godot_v4.7.2-stable_win64.exe'))
    env = {**os.environ,'ENFRACTAL_TRAVEL_PORT':str(port),'ENFRACTAL_TRAVEL_TOKEN':local_config['token']}
    # The fixture creates its own old workshop before opting into travel.
    for key in ['ENFRACTAL_SAVE_TRAVEL','ENFRACTAL_MANUAL_INVENTION','ENFRACTAL_START_WALK']:
        env.pop(key,None)
    process = None
    try:
        for stage in ['first','restart']:
            with (sandbox/f'service-{stage}.out').open('w') as output, (sandbox/f'service-{stage}.err').open('w') as error:
                process = subprocess.Popen([sys.executable,str(ROOT/'services/save_travel/server.py'),'--config',str(config_path),'--port',str(port)],cwd=ROOT,stdout=output,stderr=error,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
                deadline = time.monotonic()+15
                while True:
                    if process.poll() is not None:
                        raise RuntimeError('Isolated service could not start; see redacted service logs')
                    try:
                        with socket.create_connection(('127.0.0.1',port),timeout=.2): break
                    except OSError:
                        if time.monotonic()>deadline: raise TimeoutError('Service startup timed out')
                        time.sleep(.1)
                env['ENFRACTAL_TRAVEL_TEST_RESUME'] = '1' if stage=='restart' else '0'
                command = [str(engine),'--path',str(ROOT/'game'),'--script','res://tests/save_travel_integration.gd']
                if not args.capture: command.insert(1,'--headless')
                run = subprocess.run(command,cwd=ROOT,env=env,capture_output=True,text=True,timeout=60,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
                (sandbox/f'game-{stage}.out').write_text(run.stdout,encoding='utf-8')
                (sandbox/f'game-{stage}.err').write_text(run.stderr,encoding='utf-8')
                print(run.stdout.strip().splitlines()[-1] if run.stdout.strip() else 'No game output')
                if run.returncode or run.stderr.strip():
                    print(run.stderr)
                    raise RuntimeError(f'Actual map {stage} failed; inspect {sandbox.name}')
                process.terminate()
                process.wait(timeout=10)
                process = None
        print('Actual PostgreSQL/service/game restart integration passed; no personal world changed.')
    finally:
        if process and process.poll() is None:
            process.terminate()
            process.wait(timeout=10)
        # Only the exact, freshly created fixture database may be removed.
        if not database.startswith(f'enfractal_integration_{os.getpid()}_'):
            raise RuntimeError('Refusing unknown database cleanup')
        with psycopg.connect(**admin_args,autocommit=True) as connection:
            connection.execute(sql.SQL('DROP DATABASE {} WITH (FORCE)').format(sql.Identifier(database)))

if __name__=='__main__':
    main()
