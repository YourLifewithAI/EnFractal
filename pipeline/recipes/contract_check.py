"""Execute the live contract's standalone GLB check without schema dependencies.

Only these three exact AST definitions are loaded, never a substitute validator.
If the contract adds dependencies, this adapter fails closed until reviewed.
"""
import ast
import json
import math
import struct
from pathlib import Path


def check_glb(data, label):
    path = Path(__file__).resolve().parents[2] / 'contracts' / 'validate.py'
    tree = ast.parse(path.read_text(encoding='utf-8'), filename=str(path))
    names = {'ContractError', 'loads_strict', 'check_glb'}
    nodes = [n for n in tree.body if isinstance(n, (ast.FunctionDef, ast.ClassDef))
             and n.name in names]
    if {n.name for n in nodes} != names:
        raise RuntimeError('contract GLB entry points changed; adapter needs review')
    namespace = {'json': json, 'math': math, 'struct': struct}
    exec(compile(ast.Module(body=nodes, type_ignores=[]), str(path), 'exec'), namespace)
    return namespace['check_glb'](data, label)
