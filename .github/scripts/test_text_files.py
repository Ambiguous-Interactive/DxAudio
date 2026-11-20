"""Controls for the UTF-8 and LF repository policy."""

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.dont_write_bytecode = True

import check_text_files


class TextFilePolicyTests(unittest.TestCase):
    def test_source_encoding_and_line_endings(self):
        check = check_text_files.text_problems
        self.assertEqual([], check("café\n".encode(), final_newline=True))
        self.assertEqual(["unexpected UTF-8 BOM"], check(b"\xef\xbb\xbftext\n", final_newline=True))
        self.assertEqual(["expected LF line endings"], check(b"text\r\n", final_newline=True))
        self.assertEqual(["missing final newline"], check(b"text", final_newline=True))
        self.assertEqual(["invalid UTF-8"], check(b"\xff\n", final_newline=True))

    def test_spelling_words_are_unique_ignoring_case(self):
        self.assertEqual([], check_text_files.spelling_problems(["DxAudio", "multiband"]))
        self.assertEqual(["case-redundant spelling word: dxaudio"], check_text_files.spelling_problems(["DxAudio", "dxaudio"]))

    def test_index_and_worktree_are_checked_separately(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            subprocess.run(["git", "init", "-q"], cwd=root, check=True)
            path = root / "data.txt"
            path.write_bytes(b"text\r\n")
            subprocess.run(["git", "-c", "core.autocrlf=false", "add", "data.txt"], cwd=root, check=True)
            path.write_bytes(b"text\n")
            with patch.object(check_text_files, "ROOT", root):
                self.assertEqual(["data.txt: index is not normalized to LF"], check_text_files.index_problems())
                subprocess.run(["git", "-c", "core.autocrlf=false", "add", "data.txt"], cwd=root, check=True)
                self.assertEqual([], check_text_files.index_problems())


if __name__ == "__main__":
    unittest.main()
