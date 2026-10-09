"""Run from the repository root: python -m unittest pipeline.recipes.tests -v."""
import unittest


def load_tests(loader, tests, pattern):
    from . import test_recipes
    return loader.loadTestsFromModule(test_recipes)
