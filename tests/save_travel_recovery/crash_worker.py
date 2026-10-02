"""Disposable test process: deliberately exits at an acknowledged portal checkpoint."""
import json
import os
from pathlib import Path
import sys
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "services/save_travel"))
from service import Service

payload = json.load(sys.stdin)
service = Service(payload["database_url"])
response = {}


def call(op, **fields):
    global response
    request = {"op": op, "action_id": uuid.uuid4().hex, **fields}
    if op != "bootstrap":
        request.update(session_id=response["session_id"], world_id=response["world_id"], presence_epoch=response["presence_epoch"])
    response = service.dispatch("local_player", request)
    if not response.get("ok"):
        print(json.dumps(response), file=sys.stderr, flush=True)
        raise SystemExit(1)
    return response


fixture = payload["fixture"]
call("bootstrap", base_pin=fixture["empty"]["base_pin"], initial_home=fixture["populated"], empty_world=fixture["empty"])
prepared = call("travel_prepare", destination_world_id=payload["destination"], envelope=response["envelope"],
                expected_store_revision=response["store_revision"], source_checkpoint=[-22.0, 0.0, 340.0])
transfer = prepared["transfer_id"]
stage = payload["stage"]
if stage != "prepared":
    call("travel_freeze", transfer_id=transfer)
if stage in ("committed", "arrived"):
    call("travel_commit", transfer_id=transfer, expected_rules_revision=prepared["destination_rules"]["revision"])
if stage == "arrived":
    call("travel_arrive", transfer_id=transfer)
# Skip Python cleanup and process shutdown hooks. PostgreSQL has already
# acknowledged the transaction; a new process must find its durable state.
os._exit(73)
