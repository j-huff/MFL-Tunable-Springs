"""Run with uv run --with UnityPy python spring-tuner/evidence/extract_torsion_defaults.py."""
from pathlib import Path
import json,struct
import UnityPy
root=Path(__file__).resolve().parents[2]
for o in UnityPy.load(str(root/'Mars First Logistics_Data/sharedassets0.assets')).objects:
    if o.path_id not in (99992,99993,99994): continue
    r=o.get_raw_data();n=struct.unpack_from('<I',r,28)[0]
    print(json.dumps(dict(path_id=o.path_id,name=r[32:32+n].decode(),
        id=struct.unpack_from('<i',r,(32+n+3)&~3)[0],
        stiffness=struct.unpack_from('<f',r,len(r)-8)[0],
        damping=struct.unpack_from('<f',r,len(r)-4)[0])))
