import importlib.util
from pathlib import Path
p = Path(__file__).resolve().parent / "Run-H1D10D1.py"
spec = importlib.util.spec_from_file_location("h1d10d1", p)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
canonical = m.canonical
detect_title_candidates = m.detect_title_candidates
