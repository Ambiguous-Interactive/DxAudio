"""Controls for deterministic Unity companion generation."""

import contextlib
import io
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.dont_write_bytecode = True

import meta_files


class MetaGenerationTests(unittest.TestCase):
    def test_generates_importers_without_rewriting_guid_or_ignored_content(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Runtime"
            ignored = runtime / "Draft~"
            ignored.mkdir(parents=True)
            (ignored / "Hidden.cs").touch()
            (ignored / "Orphan.cs.meta").touch()
            (runtime / "Audio.cs").touch()
            (runtime / "Audio.asmdef").touch()
            existing = runtime / "Audio.cs.meta"
            original = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nMonoImporter:\n"
            existing.write_text(original, encoding="utf-8")
            with patch.object(meta_files, "ROOT", root), contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                with patch.object(sys, "argv", ["meta_files", "--check"]):
                    self.assertEqual(1, meta_files.main())
                with patch.object(sys, "argv", ["meta_files"]):
                    self.assertEqual(0, meta_files.main())
                with patch.object(sys, "argv", ["meta_files", "--check"]):
                    self.assertEqual(0, meta_files.main())
                assembly_meta = runtime / "Audio.asmdef.meta"
                self.assertEqual(meta_files.meta_text(runtime / "Audio.asmdef"), assembly_meta.read_text(encoding="utf-8"))
                self.assertIn("AssemblyDefinitionImporter:", assembly_meta.read_text(encoding="utf-8"))
                self.assertEqual(original, existing.read_text(encoding="utf-8"))
                self.assertFalse((ignored / "Hidden.cs.meta").exists())


if __name__ == "__main__":
    unittest.main()
