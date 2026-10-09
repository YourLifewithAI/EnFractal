"""Independent decoding of canonical DEFLATE tokens and PNG Sub filtering."""

import io
import unittest
import zlib

from PIL import Image

from pipeline.landscape.corpus.preview import Canvas, deflate_zero_runs


class PreviewTests(unittest.TestCase):
    def test_deflate_roundtrips_literals_and_every_match_length(self):
        # All literal codes, all length/extra-bit transitions, multiple copies,
        # runs at either end, and trailing one/two bytes after a 258-byte copy.
        cases = [b"", bytes(range(256)), b"\0"*65536]
        cases += [b"\xff"+b"\0"*n+b"\x80" for n in range(1,523)]
        for data in cases:
            with self.subTest(size=len(data)):
                self.assertEqual(zlib.decompress(deflate_zero_runs(data)),data)

    def test_png_sub_filter_preserves_rgb_bytes_across_row_boundaries(self):
        for width in (1,2,17,300):
            canvas = Canvas(width,3)
            for x in range(width):
                canvas.pixel(x,0,(x%256,(255-x)%256,(17*x)%256))
                canvas.pixel(x,2,(0,0,0))
            with self.subTest(width=width):
                with Image.open(io.BytesIO(canvas.png())) as decoded:
                    self.assertEqual(decoded.mode,"RGB")
                    self.assertEqual(decoded.size,(width,3))
                    self.assertEqual(decoded.tobytes(),bytes(canvas.pixels))
