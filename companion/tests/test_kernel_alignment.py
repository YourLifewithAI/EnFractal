"""The mock host against the kernel host it stands in for (Lane P's CommandHost.cs and creation_authority.gd).

Run 2 swaps the mock for the kernel host behind the same link, so every number both of them fix must be
the same. These tests read the kernel's own constants (read-only) and fail when either side drifts, then
check the mock's default policy at those boundaries.
"""
from __future__ import annotations

import re
import unittest

from support import COMPANION, PLAYER, REPO, FakeClock, HostPolicy, command, new_host, query

from enfractal_companion.mock_host import Receipt

COMMAND_HOST = REPO / "game" / "scripts" / "native" / "Kernel" / "CommandHost.cs"
AUTHORITY = REPO / "game" / "scripts" / "creation_authority.gd"


def _constant(path, pattern: str) -> int:
    match = re.search(pattern, path.read_text(encoding="utf-8"))
    if match is None:
        raise AssertionError(f"{path.name} no longer declares {pattern!r}; update this test with the kernel")
    return int(match.group(1))


@unittest.skipUnless(COMMAND_HOST.is_file() and AUTHORITY.is_file(), "the kernel host is not in this checkout")
class PolicyMatchesTheKernel(unittest.TestCase):
    def test_the_mock_hover_and_default_name_match_the_real_gubble(self):
        from enfractal_companion.mock_host import HOVER_M
        body = (REPO / "game/scripts/native/SmallPlayerController.cs").read_text(encoding="utf-8")
        avatar = (REPO / "game/scripts/native/CompanionAvatar.cs").read_text(encoding="utf-8")
        hover = re.search(r"\bfloat HoverHeightM \{ get; set; \} = ([0-9.]+)f;", body)
        self.assertIsNotNone(hover)
        self.assertEqual(HOVER_M, float(hover.group(1)))
        name = re.search(r'\bconst string DefaultName = "([^"]+)";', avatar)
        self.assertIsNotNone(name)
        self.assertEqual(new_host().entities["avatar:companion"].display_name, name.group(1))

    def test_the_default_policy_uses_the_kernel_hosts_numbers(self):
        policy = HostPolicy()
        cs = lambda name: _constant(COMMAND_HOST, rf"\bconst int {name} = (\d+);")  # noqa: E731
        gd = lambda name: _constant(AUTHORITY, rf"\bconst {name} := (\d+)\b")  # noqa: E731
        expected = {
            "max_pending_approvals": cs("MaxPendingApprovals"),
            "companion_messages_per_s": cs("CompanionMessagesPerSecond"),
            "max_transient_receipts_per_principal": cs("MaxTransientPerPrincipal"),
            "perception_memory_entries": cs("PerceptionMemoryEntries"),
            "perception_memory_stale_after_s": cs("PerceptionMemoryStaleAfterS"),
            "max_jobs_per_principal": cs("MaxJobsPerPrincipal"),
            "max_durable_receipts": gd("MAX_RECEIPTS"),
            "player_receipt_reserve": gd("PLAYER_RECEIPT_RESERVE"),
            "max_compacted_receipts": gd("MAX_COMPACTED"),
            "max_checkpoints": gd("MAX_CHECKPOINTS"),
            "approval_ttl_s": 60 * _constant(COMMAND_HOST, r"ApprovalLifetime = TimeSpan\.FromMinutes\((\d+)\)"),
        }
        self.assertEqual({name: getattr(policy, name) for name in expected}, expected)
        # Run 2: the walking goals' timeout, the team's shared sight and the journal's bounds.
        journal = REPO / "game" / "scripts" / "native" / "Kernel" / "CommandHostJournal.cs"
        self.assertEqual(policy.unreachable_after_s,
                         float(_constant(COMMAND_HOST, r"\bconst double UnreachableAfterS = (\d+)\.0;")))
        self.assertIn("public static bool DefaultSharedSight { get; set; } = true;", COMMAND_HOST.read_text(encoding="utf-8"))
        self.assertTrue(policy.shared_sight)
        from enfractal_companion import mock_journal
        self.assertEqual((mock_journal.MAX_OPEN_TASKS, mock_journal.MAX_HISTORY, mock_journal.MAX_NOTES),
                         tuple(_constant(journal, rf"\bconst int {name} = (\d+);") for name in ("MaxOpenTasks", "MaxHistory", "MaxNotes")))
        from enfractal_companion.mock_host import WALKING_GOALS
        self.assertIn('WalkingGoals = new() { "come", "go_to", "fetch" }', COMMAND_HOST.read_text(encoding="utf-8"))
        self.assertEqual(WALKING_GOALS, {"come", "go_to", "fetch"})
        # The activation receipts the authority keeps are bounded the same way as the host's transient ones.
        self.assertEqual(gd("MAX_TRANSIENT_PER_PRINCIPAL"), policy.max_transient_receipts_per_principal)

    def test_observe_sees_the_whole_island_and_bounds_a_given_radius_as_the_contract_does(self):
        """The 10x world (S2): observe has no distance cut-off unless radius_m narrows it, and the contract, the kernel
        and the mock bound radius_m alike."""
        import json
        import math
        args = json.loads((REPO / "contracts" / "game-command.schema.json").read_bytes())["$defs"]["args"]["observe"]
        maximum = _constant(COMMAND_HOST, r"\bconst int ObserveRadiusMaxM = (\d+);")
        self.assertEqual((args["properties"]["radius_m"]["maximum"], maximum), (150, 150))
        self.assertNotIn("radius_m", args["required"])
        source = COMMAND_HOST.read_text(encoding="utf-8")
        self.assertIn('var radius = args.TryGetProperty("radius_m", out var r) ? (float)CanonicalJson.ReadNumber(r) : float.PositiveInfinity;', source)
        self.assertIn('radius.GetDouble() is <= 0 or > ObserveRadiusMaxM', source)
        self.assertEqual(HostPolicy().observe_max_radius_m, math.inf)

    def test_near_reaches_as_far_in_the_contract_the_kernel_and_the_mock(self):
        """Play distances (S8): entities.list's near.radius_m is at most 150 m (was 50) in the contract, the kernel and the mock."""
        import json
        near = json.loads((REPO / "contracts" / "game-command.schema.json").read_bytes())["$defs"]["args"]["entities.list"]
        maximum = near["properties"]["filter"]["properties"]["near"]["properties"]["radius_m"]["maximum"]
        self.assertEqual((maximum, _constant(COMMAND_HOST, r"\bconst int NearRadiusMaxM = (\d+);")), (150, 150))
        host = new_host()
        reach = {"filter": {"near": {"center_m": [0, 0, 0], "radius_m": 150}}}
        self.assertTrue(host.handle(COMPANION, query("entities.list", reach))["ok"])
        past = host.handle(COMPANION, query("entities.list", {"filter": {"near": {"center_m": [0, 0, 0], "radius_m": 151}}}, "q-2"))
        self.assertEqual((past["ok"], past["error"]["field_path"]), (False, "$.args.filter.near.radius_m"))

    def test_entity_ids_are_the_contracts_in_the_kernel(self):
        """A plant copy's id (plant:<set>-<row>) is well formed in both hosts, so both answer target_not_found for a copy
        they do not know, and both refuse a second spelling of the same copy."""
        import json
        common = json.loads((REPO / "contracts" / "common.schema.json").read_bytes())["$defs"]
        kernel = re.search(r'Regex EntityId = new\(@"\\A(.+)\\z", RegexOptions\.Compiled\);', COMMAND_HOST.read_text(encoding="utf-8"))
        self.assertIsNotNone(kernel)
        self.assertEqual(kernel.group(1), common["entity_id"]["pattern"].removeprefix("^").removesuffix("$"))
        self.assertTrue(common["plant_id"]["pattern"].removeprefix("^").removesuffix("$") in kernel.group(1))
        host = new_host()
        known = host.handle(COMPANION, query("entity.inspect", {"target": "plant:broadleaf-0"}))
        self.assertEqual(known["error"]["code"], "target_not_found")
        # A second spelling of the same copy is malformed in both (the kernel's CommandHostTest checks request_invalid;
        # the mock maps any argument the schema refuses to invalid_args, for every malformed id alike).
        second = host.handle(COMPANION, query("entity.inspect", {"target": "plant:broadleaf-007"}, "q-2"))
        self.assertEqual((second["ok"], second["error"]["field_path"]), (False, "$.args.target"))
        self.assertNotEqual(second["error"]["code"], "target_not_found")


KERNEL = REPO / "game" / "scripts" / "native"
EFFECTS = KERNEL / "Kernel" / "CommandHostEffects.cs"
RULES = KERNEL / "Kernel" / "IslandRules.cs"


def _text(path) -> str:
    return path.read_text(encoding="utf-8")


def _match(path, pattern: str) -> str:
    match = re.search(pattern, _text(path))
    if match is None:
        raise AssertionError(f"{path.name} no longer declares {pattern!r}; update this test with the kernel")
    return match.group(1)


def _method(source: str, signature: str) -> str:
    """The body of one C# method: from its signature to the next member at the same indentation."""
    start = source.index(signature)
    end = re.search(r"\n    (?:private|public|internal)\b", source[start + len(signature):])
    return source[start:start + len(signature) + (end.start() if end else len(source))]


@unittest.skipUnless(EFFECTS.is_file() and RULES.is_file(), "the kernel host's abilities are not in this checkout")
class GlowMatchesTheKernel(unittest.TestCase):
    """The island's abilities (Run 2, Glow): the mock's effect.start and capabilities.list against the kernel's."""

    def test_the_effect_numbers_are_the_kernels(self):
        from enfractal_companion import mock_host as m
        expected = {
            "max_active_effects": _constant(COMMAND_HOST, r"\bconst int MaxActiveEffects = (\d+);"),
            "CAPABILITIES_DEFAULT_LIMIT": _constant(COMMAND_HOST, r"\bconst int CapabilitiesDefaultLimit = (\d+);"),
            "EFFECT_SIGHT_HALF_WIDTH_M": float(_match(COMMAND_HOST, r"\bconst float EffectSightHalfWidthM = ([0-9.]+)f;")),
            "WISP_LIFT_M": float(_match(KERNEL / "Look" / "LookDirector.Glow.cs", r"\bconst float WispLiftM = ([0-9.]+)f;")),
            "MAX_RULES_PACK_BYTES": _constant(RULES, r"\bconst int MaxPackBytes = (\d+);"),
            "DEFAULT_RULES_ID": _match(KERNEL / "RoomWorld.cs", r'\bconst string DefaultRulesId = "([a-z0-9_]+)";'),
            "DEFAULT_RULES_VERSION": _constant(KERNEL / "RoomWorld.cs", r"\bconst int DefaultRulesVersion = (\d+);"),
            "SEA_EXTENSION": _match(KERNEL / "Room" / "RoomSea.cs", r'\bconst string ExtensionName = "([a-z0-9_]+)";'),
            "GUBBLE": _match(COMMAND_HOST, r'\bconst string CompanionAvatarId = "([a-z:]+)";'),
            "EFFECT_ID_ALPHABET": _match(EFFECTS, r'"effect:" \+ System\.Security\.Cryptography\.RandomNumberGenerator\.GetString\("([a-z2-7]+)", \d+\)'),
            "EFFECT_ID_LENGTH": int(_match(EFFECTS, r'RandomNumberGenerator\.GetString\("[a-z2-7]+", (\d+)\)')),
        }
        actual = {name: getattr(HostPolicy(), name) if name == "max_active_effects" else getattr(m, name) for name in expected}
        self.assertEqual(actual, expected)

    def test_the_mock_reads_the_pack_the_kernel_loads(self):
        import hashlib
        from enfractal_companion.mock_host import rules_path
        self.assertIn('=> $"res://rules/{rulesId}/v{rulesVersion}.json";', _text(RULES))
        path = rules_path()
        self.assertEqual(path, REPO / "game" / "rules" / "storybook_wild" / "v4.json")
        host = new_host()
        self.assertEqual((host.rules.rules_id, host.rules.rules_version, host.rules.sha256),
                         ("storybook_wild", 4, hashlib.sha256(path.read_bytes()).hexdigest()))
        self.assertEqual([(a.capability, a.primitive) for a in host.rules.abilities],
                         [("glow", "light.emit"), ("bubbles", "particles.float"), ("fireworks", "particles.burst"),
                          ("bloom", "growth.seed"), ("fire", "element.ignite")])

    def test_the_mock_loads_the_primitives_the_kernel_carries_out(self):
        """The mock checks a pack against the island-rules schema; the kernel against its own table of primitives
        (IslandRules.Primitives) and then hands each to the look by primitive (StartLook). All three must name the same
        primitives with the same outer limits, or one host would load a pack the other refuses."""
        import json
        schema = json.loads((REPO / "contracts" / "island-rules.schema.json").read_bytes())
        in_schema = {}
        for rule in schema["$defs"]["ability"].get("allOf", []):
            primitive = rule.get("if", {}).get("properties", {}).get("primitive", {}).get("const")
            if primitive is None:
                continue
            then = rule["then"]["properties"]
            in_schema[primitive] = (
                then["category"]["const"],
                {name: (p["properties"]["min"]["minimum"], p["properties"]["max"]["maximum"])
                 for name, p in then["params"]["properties"].items()},
                then["reach_m"]["maximum"], then["area_radius_max_m"]["maximum"], then["duration_max_s"]["maximum"],
                then["max_active"]["maximum"])
        table = _match(RULES, r"Primitives = new Dictionary<string, PrimitiveLimits>\(StringComparer\.Ordinal\)\s*\{([\s\S]*?)\n    \};")
        in_kernel = {}
        for entry in re.finditer(r'\["([a-z.]+)"\] = new\("([a-z]+)", new Dictionary<string, \(double, double\)>\(StringComparer\.Ordinal\) '
                                 r'\{ ([^}]*) \}, ([0-9.]+), ([0-9.]+), ([0-9.]+), (\d+)\)', table):
            params = {name: (float(low), float(high))
                      for name, low, high in re.findall(r'\["([a-z_]+)"\] = \(([0-9.]+), ([0-9.]+)\)', entry.group(3))}
            in_kernel[entry.group(1)] = (entry.group(2), params, float(entry.group(4)), float(entry.group(5)),
                                         float(entry.group(6)), int(entry.group(7)))
        self.assertEqual(len(in_kernel), table.count("] = new("), "a kernel primitive this test could not read")
        self.assertEqual(in_kernel, in_schema)
        self.assertEqual(sorted(in_kernel), ["element.ignite", "growth.seed", "light.emit", "particles.burst", "particles.float"])
        start_look = _method(_text(EFFECTS), "private bool StartLook(")
        self.assertEqual(sorted(re.findall(r'"([a-z]+\.[a-z]+)" => look\.Start\w+\(', start_look)),
                         ["light.emit", "particles.burst", "particles.float"])
        # Persistent terrain primitives use their own authority path, never a cosmetic effect receipt.
        plan = _method(_text(EFFECTS), "private EffectPlan PlanEffect(")
        self.assertIn('capability is "bloom" or "fire"', plan)
        self.assertIn("Use nature.cast for this ability; preview Fire first.", plan)
        nature = _text(KERNEL / "Kernel" / "CommandHostNature.cs")
        self.assertIn('if (op == "nature.cast")', nature)
        self.assertIn('Rules?.Ability(capability)', nature)

    def test_reserved_param_names_are_the_contracts_everywhere(self):
        import json
        command_schema = json.loads((REPO / "contracts" / "game-command.schema.json").read_bytes())
        rules_schema = json.loads((REPO / "contracts" / "island-rules.schema.json").read_bytes())
        contract = command_schema["$defs"]["args"]["effect.start"]["properties"]["params"]["propertyNames"]["not"]["enum"]
        pack = rules_schema["$defs"]["ability"]["properties"]["params"]["propertyNames"]["not"]["enum"]
        pattern = r"ReservedParams = \{ ([^}]+) \};"
        kernel = [re.findall(r'"([a-z_]+)"', _match(path, pattern)) for path in (COMMAND_HOST, RULES)]
        self.assertEqual([contract, kernel[0], kernel[1]], [pack, pack, pack])

    def test_the_mock_checks_effect_start_in_the_kernels_order_and_words(self):
        """The codes PlanEffect throws, in order, are the mock's _plan_effect's, the water surface's two included; and every
        refusal message and field path the mock gives for the abilities is the kernel's, word for word."""
        import ast
        import inspect
        import textwrap
        from enfractal_companion.mock_host import MockHost
        kernel = _method(_text(EFFECTS), "private EffectPlan PlanEffect(")
        mock = inspect.getsource(MockHost._plan_effect)
        self.assertEqual(re.findall(r'HostError\("(\w+)"', mock), re.findall(r'new Refusal\("(\w+)"', kernel))
        words = set()
        for function in (MockHost._plan_effect, MockHost._op_effect_start, MockHost._op_effect_stop):
            tree = ast.parse(textwrap.dedent(inspect.getsource(function)))
            for node in ast.walk(tree):
                if isinstance(node, ast.Call) and getattr(node.func, "id", "") == "HostError":
                    for value in list(node.args[1:2]) + [k.value for k in node.keywords if k.arg == "field_path"]:
                        for constant in ast.walk(value):
                            if isinstance(constant, ast.Constant) and isinstance(constant.value, str):
                                words.add(constant.value)
        source = _text(EFFECTS)
        self.assertGreater(len(words), 20)
        self.assertEqual(sorted(w for w in words if w not in source), [])
        # capabilities.list's one refusal, a cursor it did not give.
        cursor = "That cursor is not from this list."
        self.assertIn(cursor, _method(source, "private JsonObject Capabilities("))
        self.assertIn(cursor, inspect.getsource(MockHost._query))


NATURE = KERNEL / "Kernel" / "CommandHostNature.cs"


@unittest.skipUnless(NATURE.is_file(), "the kernel host's nature is not in this checkout")
class NatureMatchesTheKernel(unittest.TestCase):
    """Nature (contracts/README.md "Nature"). The mock does not simulate it yet: it checks the nature commands as the kernel
    does before its nature runs, then answers unavailable (the one exception test_host_alignment.py allows). What it does
    check must be the kernel's: the args (CheckNatureArgs), and room.undo of any change that is not nature's (NatureUndo)."""

    def test_the_mock_checks_the_nature_commands_the_kernel_has(self):
        from enfractal_companion.mock_host import NATURE_OPS
        check = _method(_text(NATURE), "private static void CheckNatureArgs(")
        self.assertEqual(set(re.findall(r'"(nature\.[a-z]+)" =>', check)), set(NATURE_OPS))
        # Its refusals are field_unknown and request_invalid only, which is what the mock's mapping gives (refusals.py).
        self.assertEqual(set(re.findall(r'new Refusal\("(\w+)"', check)), {"field_unknown", "request_invalid"})

    def test_the_contract_bounds_the_args_as_the_kernel_does(self):
        import json
        defs = json.loads((REPO / "contracts" / "game-command.schema.json").read_bytes())["$defs"]
        cast = defs["args"]["nature.cast"]["properties"]
        check = _method(_text(NATURE), "private static void CheckNatureArgs(")
        self.assertIn('is not ("bloom" or "fire")', check)
        self.assertEqual(cast["capability"]["enum"], ["bloom", "fire"])
        self.assertIn('("radius_m", NatureBlock.MaxFireRadiusM), ("duration_s", 600f)', check)
        radius = float(_match(KERNEL / "Kernel" / "NatureStore.cs", r"\bconst float MaxFireRadiusM = ([0-9.]+)f;"))
        self.assertEqual((cast["radius_m"]["maximum"], cast["duration_s"]["maximum"]), (radius, 600))
        self.assertEqual(_match(NATURE, r'PreviewIdPattern = new\(@"\\A([^"]+)\\z"'), defs["preview_id"]["pattern"].strip("^$"))
        self.assertEqual(_match(COMMAND_HOST, r'CreationId = new\(@"\\A([^"]+)\\z"'), defs["creation_id"]["pattern"].strip("^$"))
        for op in ("nature.cast", "nature.trail", "nature.repair"):
            with self.subTest(op=op):
                allowed = re.search(rf'"{re.escape(op)}" => new\[\] \{{([^}}]*)\}}', check).group(1)
                self.assertEqual(set(re.findall(r'"([a-z_]+)"', allowed)), set(defs["args"][op]["properties"]))

    def test_room_undo_of_any_other_change_answers_the_kernels_codes_and_words(self):
        import inspect
        from enfractal_companion import mock_host
        undo = _method(_text(NATURE), "private JsonObject NatureUndo(")
        for words in (mock_host.UNDO_NOT_EARLIER, mock_host.UNDO_ONLY_NATURE):
            self.assertIn(f'"{words}"', undo)
        # After the kernel's nature and role checks (not_ready, permission_denied), the refusals the mock gives, in order.
        in_kernel = re.findall(r'new Refusal\("(\w+)"', undo)
        self.assertEqual(in_kernel[:2], ["invalid_args", "unsupported_capability"])
        self.assertEqual(re.findall(r'HostError\("(\w+)"', inspect.getsource(mock_host.MockHost._op_room_undo)), in_kernel[:2])
        self.assertIn('"room.undo" when !NatureConfigured', _text(COMMAND_HOST))

    def test_outside_creative_undo_is_refused_first_in_the_kernels_words(self):
        """CommandHost.Unsupported refuses room.undo outside Creative before CheckExpectations; the mock's _check_unsupported
        does the same before _check_revisions, with the same words."""
        import inspect
        from enfractal_companion import mock_host
        host = _text(COMMAND_HOST)
        self.assertIn(f'"room.undo" when Mode != "creative" => "{mock_host.UNDO_NOT_IN_THIS_MODE}",', host)
        execute = _method(host, "private JsonObject Execute(")
        self.assertLess(execute.index("Unsupported(op, args, principal)"), execute.index("CheckExpectations(root, op, args, principal)"))
        self.assertIn('throw new Refusal("unsupported_capability", unsupported, "$.op")', execute)
        command = inspect.getsource(mock_host.MockHost._command)
        self.assertLess(command.index("self._check_unsupported(message)"), command.index("self._check_revisions(principal, message)"))
        self.assertEqual(re.findall(r'HostError\("(\w+)"', inspect.getsource(mock_host.MockHost._check_unsupported)), ["unsupported_capability"])


EYES = KERNEL / "Kernel" / "CommandHostEyes.cs"


def _cs_string(path, name: str) -> str:
    """A C# string constant's value (the kernel's words use only the escapes \\" and \\\\)."""
    raw = _match(path, rf'\bpublic const string {name} = "((?:[^"\\]|\\.)*)";')
    return raw.replace('\\"', '"').replace("\\\\", "\\")


@unittest.skipUnless(EYES.is_file(), "the kernel host's eyes are not in this checkout")
class EyesMatchTheKernel(unittest.TestCase):
    """The Gubble's eyes (view.look): the mock's numbers and words are the kernel's, and the contract's bounds hold them."""

    def test_the_numbers_are_the_kernels(self):
        from enfractal_companion import eyes
        expected = {
            "LOOK_BURST": _constant(EYES, r"\bconst int LookBurst = (\d+);"),
            "LOOK_REFILL_S": float(_match(EYES, r"\bconst double LookRefillS = ([0-9.]+);")),
            "LOOK_IMAGE_MAX_BYTES": _constant(EYES, r"\bconst int LookImageMaxBytes = (\d+);"),
            "LOOK_WIDTH_PX": _constant(EYES, r"\bconst int LookWidthPx = (\d+);"),
            "LOOK_HEIGHT_PX": _constant(EYES, r"\bconst int LookHeightPx = (\d+);"),
            "LOOK_MAX_YAW_DEG": float(_match(EYES, r"\bconst double LookMaxYawDeg = ([0-9.]+);")),
            "LOOK_MAX_PITCH_DEG": float(_match(EYES, r"\bconst double LookMaxPitchDeg = ([0-9.]+);")),
            "EYES_PROFILE_KEY": _cs_string(EYES, "EyesProfileKey"),
            "DEFAULT_GUBBLE_EYES": _match(EYES, r"\bconst bool DefaultGubbleEyes = (true|false);") == "true",
            "EYE_VERTICAL_FOV_DEG": float(_match(KERNEL / "SmallPlayerController.cs",
                                                 r'Name = "EyeCamera",[^\n]*\n[^\n]*\bFov = ([0-9.]+)f')),
        }
        self.assertEqual({name: getattr(eyes, name) for name in expected}, expected)
        self.assertEqual((HostPolicy().look_burst, HostPolicy().look_refill_s), (expected["LOOK_BURST"], expected["LOOK_REFILL_S"]))

    def test_the_refusals_are_the_kernels_word_for_word_and_in_its_order(self):
        import inspect
        from enfractal_companion import eyes
        from enfractal_companion.mock_host import MockHost
        for mock, kernel in (("EYES_OFF_MESSAGE", "EyesOffMessage"), ("NO_RENDERER_MESSAGE", "NoRendererMessage"),
                             ("LOOK_RATE_MESSAGE", "LookRateMessage"), ("LOOK_ACTOR_MESSAGE", "LookActorMessage")):
            with self.subTest(message=mock):
                self.assertEqual(getattr(eyes, mock), _cs_string(EYES, kernel))
        view_look = _method(_text(EYES), "private JsonObject ViewLook(")
        in_kernel = [code for code in re.findall(r'new Refusal\("(\w+)"', view_look) if code != "not_ready"]
        in_mock = re.findall(r'HostError\("(\w+)"', inspect.getsource(MockHost._view_look))
        # The kernel also refuses a caller that cannot wait for a frame (its synchronous path), which the mock never needs.
        self.assertEqual(in_kernel, in_mock + ["unavailable"])
        self.assertEqual(in_mock, ["actor_denied", "permission_denied", "rate_limited", "unavailable"])

    def test_the_contract_bounds_are_the_kernels(self):
        import json
        from enfractal_companion import eyes
        defs = json.loads((REPO / "contracts" / "game-command.schema.json").read_bytes())["$defs"]
        if "view.look" not in defs["query_op"]["enum"]:
            self.skipTest("the contract has no view.look yet")
        args, image = defs["args"]["view.look"]["properties"], defs["data"]["view.look"]["properties"]["image"]["properties"]
        self.assertEqual((args["yaw_deg"]["minimum"], args["yaw_deg"]["maximum"]), (-eyes.LOOK_MAX_YAW_DEG, eyes.LOOK_MAX_YAW_DEG))
        self.assertEqual((args["pitch_deg"]["minimum"], args["pitch_deg"]["maximum"]), (-eyes.LOOK_MAX_PITCH_DEG, eyes.LOOK_MAX_PITCH_DEG))
        self.assertEqual(image["bytes"]["maximum"], eyes.LOOK_IMAGE_MAX_BYTES)
        self.assertEqual(image["data_base64"]["maxLength"], 4 * -(-eyes.LOOK_IMAGE_MAX_BYTES // 3))
        self.assertGreaterEqual(image["width_px"]["maximum"], max(eyes.LOOK_WIDTH_PX, eyes.LOOK_HEIGHT_PX))
        self.assertIn("unavailable", defs["error_code"]["enum"])
        # The largest picture leaves the result well inside the kernel's 256 KiB answer (and so the link's response frame).
        self.assertLess(image["data_base64"]["maxLength"] + 8192, _constant(COMMAND_HOST, r"\bconst int MaxResultBytes = (\d+);"))


class DefaultPolicyBoundaries(unittest.TestCase):
    """The default policy at the kernel's boundaries (the mechanisms are tested with small numbers elsewhere)."""

    def setUp(self):
        self.clock = FakeClock()
        self.host = new_host(clock=self.clock)

    def tearDown(self):
        from support import contract_problems
        for result in self.host.emitted:
            self.assertEqual(contract_problems(result), [], result)

    def fill_ledger(self, count: int) -> None:
        """Durable receipts committed earlier (contents do not matter for the bound)."""
        for i in range(len(self.host.receipts), count):
            self.host.receipts[(PLAYER, f"earlier-{i}")] = Receipt("0" * 64, {"revision": 0, "op": "protect.lock"}, True)

    def lock(self, target, action_id, principal=COMPANION):
        self.clock.advance(1.1)  # keep the companion well inside its rate budget
        return self.host.handle(principal, command("protect.lock", {"targets": [target]}, action_id,
                                                   expected_entities={target: self.host.entities[target].revision}))

    def test_the_companion_fills_1792_receipts_and_the_player_the_last_256(self):
        self.fill_ledger(2048 - 256 - 1)
        self.assertTrue(self.lock("obj:box", "lock-1")["ok"])
        refused = self.lock("obj:book", "lock-2")
        self.assertFalse(refused["ok"])
        self.assertEqual(refused["error"]["code"], "receipt_limit")
        self.assertTrue(self.lock("obj:book", "p-lock-1", PLAYER)["ok"])
        self.fill_ledger(2048 - 1)
        self.assertTrue(self.lock("obj:rug", "p-lock-2", PLAYER)["ok"])
        self.assertEqual(self.lock("obj:doorstop", "p-lock-3", PLAYER)["error"]["code"], "receipt_limit")

    def test_a_companion_gets_30_messages_a_second(self):
        results = [self.host.handle(COMPANION, query("room.describe", {}, f"q-{i}")) for i in range(31)]
        self.assertTrue(all(r["ok"] for r in results[:30]))
        self.assertEqual(results[30]["error"]["code"], "rate_limited")
        self.clock.advance(1.01)
        self.assertTrue(self.host.handle(COMPANION, query("room.describe", {}, "q-late"))["ok"])


if __name__ == "__main__":
    unittest.main()
