#!/usr/bin/env python3
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('tools', Path(__file__).with_name('verify-build-tool.py'))
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)

class ToolHashTests(unittest.TestCase):
    def test_corrupt_and_unknown_tools_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'tool.zip'; path.write_bytes(b'approved archive')
            expected = hashlib.sha256(path.read_bytes()).hexdigest()
            module.verify(path, hashes={'tool.zip': expected})
            path.write_bytes(b'corrupt archive')
            with self.assertRaises(ValueError): module.verify(path, hashes={'tool.zip': expected})
            with self.assertRaises(ValueError): module.verify(path, hashes={'other.zip': expected})

    def test_official_sdk_sha512_is_supported(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'sdk.zip'; path.write_bytes(b'sdk')
            module.verify(path, hashes={'sdk.zip': {'algorithm': 'sha512', 'digest': hashlib.sha512(b'sdk').hexdigest()}})

if __name__ == '__main__': unittest.main()
