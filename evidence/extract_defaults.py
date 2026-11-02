"""Read serialized defaults; run: uv run --with UnityPy python extract_defaults.py.
UnityPy provides object boundaries. This build strips script type trees, so the
specific SpringSO raw fields are decoded using confirmed metadata field order.
"""
from pathlib import Path
import json, struct
import UnityPy
root = Path(__file__).resolve().parents[2]
env = UnityPy.load(str(root / 'Mars First Logistics_Data/sharedassets0.assets'))
ids = {99934,99936,99937,99938,99939,99940,99985,99986,99987}
for obj in env.objects:
    if obj.path_id not in ids:
        continue
    assert obj.type.name == 'MonoBehaviour'
    raw = obj.get_raw_data()
    n = struct.unpack_from('<I', raw, 28)[0]
    name = raw[32:32+n].decode()
    part_id = struct.unpack_from('<i', raw, (32+n+3)&~3)[0]
    names = ('spring','damper','length','zOffset','minExtent','maxExtent','initialTargetDist')
    print(json.dumps(dict(path_id=obj.path_id, name=name, part_id=part_id,
                         **dict(zip(names, struct.unpack('<7f',raw[-28:]))))))
