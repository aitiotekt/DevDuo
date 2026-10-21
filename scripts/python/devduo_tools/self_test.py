"""Run the portable tooling test suite from a DevDuo source checkout."""

import argparse
import unittest

from .paths import repository_root


def main():
    argparse.ArgumentParser(description=__doc__).parse_args()
    suite = unittest.defaultTestLoader.discover(str(repository_root() / "tests/python"))
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    return 0 if result.wasSuccessful() else 1
