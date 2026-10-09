"""Inspect extracted ZIP/NPZ evidence; never load a game DB or enable pickle."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile
import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument("evidence")
parser.add_argument("--worker", type=Path)
args = parser.parse_args()
base = Path(args.evidence)
records = []
other_archives = []
for path in sorted((base / "probes").glob("zip-*/*.payload")):
    arrays = []
    with zipfile.ZipFile(path) as archive:
        bad = archive.testzip()
        if bad:
            raise ValueError(f"ZIP CRC failed: {bad}")
        if any(not entry.filename.endswith(".npy") for entry in archive.infolist()):
            other_archives.append({"payload": str(path), "entries": len(archive.infolist()),
                                   "status": "not-npz; unrelated archive requires its own loader"})
            continue
        for entry in archive.infolist():
            if not entry.filename.endswith(".npy"):
                raise ValueError(f"Non-NPY member: {entry.filename}")
            with archive.open(entry) as member:
                array = np.load(member, allow_pickle=False)
            arrays.append({"name": entry.filename, "dtype": str(array.dtype),
                           "shape": list(array.shape), "elements": int(array.size),
                           "bytes": entry.file_size, "allFinite": bool(np.isfinite(array).all())})
    records.append({"payload": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                    "type": "NumPyArchive", "arrays": arrays,
                    "scope": "Independent NPZ/NPY validation only; product loader still needs support."})
result = {"records": records, "arrays": sum(len(r["arrays"]) for r in records), "otherArchives": other_archives}
if args.worker:
    worker = json.loads(args.worker.read_text(encoding="utf-8"))
    payloads = {Path(r["payload"]).stem: Path(r["payload"]) for r in records}
    checked = []
    for row in worker["exported"]:
        pieces = row["id"].split("/")
        source_payload = payloads[pieces[1]]
        exported = Path(row["path"]).read_bytes()
        if row["type"] == "NumpyArchive":
            expected = source_payload.read_bytes()
        else:
            name = "/".join(pieces[2:])
            with zipfile.ZipFile(source_payload) as archive:
                expected = archive.read(name)
            array = np.load(row["path"], allow_pickle=False)
            if array.size < 0:
                raise ValueError("Invalid array size")
        if exported != expected:
            raise ValueError(f"Native export differs from source: {row['path']}")
        checked.append({"id": row["id"], "type": row["type"], "sha256": hashlib.sha256(exported).hexdigest()})
    result["workerValidation"] = {"exports": len(checked), "originalBytesIdentical": True, "checks": checked}
(base / "numpy-archive-probes.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"archives": len(records), "arrays": result["arrays"],
                  "allFinite": all(a["allFinite"] for r in records for a in r["arrays"])}))
