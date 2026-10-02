extends RefCounted
## Shared bounded traversal for live and isolated-preview capability consumers.
static func plan(artifact: Dictionary, trigger_id: String) -> Array:
	var by_id := {}
	for node in artifact.source.nodes:
		by_id[node.id] = node
	if not by_id.has(trigger_id) or by_id[trigger_id].op not in ["interact", "timer", "proximity"]:
		return []
	var reached := {trigger_id:true}
	var ordered: Array = []
	for node_id in artifact.order:
		if not reached.has(node_id):
			continue
		ordered.append(by_id[node_id].duplicate(true))
		for edge in artifact.source.edges:
			if edge.from == node_id:
				reached[edge.to] = true
	return ordered
