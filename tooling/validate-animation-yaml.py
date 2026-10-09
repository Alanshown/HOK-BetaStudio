"""Independently parse exported Unity ANIM documents; never load game DBs."""
import hashlib
import gc
import json
import math
from pathlib import Path
import re
import sys

import yaml


class UnityLoader(getattr(yaml, "CSafeLoader", yaml.SafeLoader)):
    pass


def unity_object(loader, suffix, node):
    if suffix != "74":
        raise ValueError(f"Expected Unity AnimationClip class 74, got {suffix}")
    return loader.construct_mapping(node, deep=True)


UnityLoader.add_multi_constructor("tag:unity3d.com,2011:", unity_object)
# Unity's scalar writer also emits exponent-only forms such as 1E-06.
# PyYAML's YAML 1.1 resolver otherwise leaves those valid numbers as strings.
UnityLoader.add_implicit_resolver("tag:yaml.org,2002:float", re.compile(r"^[-+]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)[eE][-+]?[0-9]+$"), list("-+0123456789."))


def tracks(clip):
    for field, prop, axes in [
        ("m_PositionCurves", "position", "xyz"),
        ("m_RotationCurves", "rotation", "xyzw"),
        ("m_ScaleCurves", "scale", "xyz"),
        ("m_EulerCurves", "euler", "xyz"),
        ("m_FloatCurves", None, ""),
    ]:
        for curve in clip.get(field) or []:
            keys = []
            previous = -math.inf
            for key in curve["curve"]["m_Curve"]:
                time = key["time"]
                values = [key["value"][axis] for axis in axes] if axes else [key["value"]]
                if not math.isfinite(time) or time < previous:
                    raise ValueError(f"Invalid/non-monotonic key time in {field}")
                if not all(isinstance(v, (float, int)) and math.isfinite(v) for v in values):
                    raise ValueError(f"Non-finite/non-numeric key value in {field}: {values}")
                # Infinite slopes legitimately encode Unity stepped tangents;
                # do not confuse them with invalid infinite key values.
                previous = time
                keys.append({"time": time, "value": values})
            if keys:
                yield {"path": curve["path"] or "", "property": prop or curve["attribute"], "keys": keys}


def main():
    root = Path(sys.argv[1]).resolve()
    source_filter = sys.argv[2]
    evidence = root / "evidence"
    latest = {}
    with (evidence / "asset-exports.jsonl").open(encoding="utf-8") as stream:
        for line in stream:
            row = json.loads(line)
            matches = Path(row["source"]).name.casefold() == source_filter.casefold() if source_filter.lower().endswith(".db") else source_filter in row["source"]
            if matches and row["type"] == "AnimationClip":
                latest[(row["id"], row["format"])] = row
    checks, failures, incomplete = [], [], []
    for (asset_id, fmt), row in latest.items():
        if fmt != "anim":
            continue
        if not row["ok"]:
            incomplete.append({"id": asset_id, "reason": row["error"]})
            continue
        documents = clip = actual = expected = None
        try:
            with Path(row["path"]).open(encoding="utf-8-sig") as stream:
                documents = list(yaml.load_all(stream, Loader=UnityLoader))
            if len(documents) != 1 or set(documents[0]) != {"AnimationClip"}:
                raise ValueError("Expected exactly one AnimationClip YAML document")
            clip = documents[0]["AnimationClip"]
            if clip["m_Name"] != row["name"]:
                raise ValueError("Exported animation name changed")
            actual = list(tracks(clip))
            curves = latest.get((asset_id, "curves-json"))
            compared = False
            if curves and curves["ok"]:
                with Path(curves["path"]).open(encoding="utf-8-sig") as stream:
                    expected = json.load(stream)["tracks"]
                if len(actual) != len(expected):
                    raise ValueError("ANIM / curves JSON track-count mismatch")
                for a, b in zip(actual, expected):
                    if a["path"] != b["path"] or a["property"] != b["property"] or len(a["keys"]) != len(b["keys"]):
                        raise ValueError("ANIM / curves JSON binding or key-count mismatch")
                    for ak, bk in zip(a["keys"], b["keys"]):
                        if len(ak["value"]) != len(bk["value"]) or not all(
                            math.isclose(x, y, rel_tol=1e-6, abs_tol=1e-7)
                            for x, y in zip([ak["time"], *ak["value"]], [bk["time"], *bk["value"]])
                        ):
                            raise ValueError("ANIM / curves JSON key-value mismatch")
                compared = True
            else:
                incomplete.append({"id": asset_id, "reason": "No successful curves JSON for comparison"})
            checks.append({"id": asset_id, "name": row["name"], "tracks": len(actual),
                           "keys": sum(len(t["keys"]) for t in actual), "curvesJsonCompared": compared})
        except Exception as error:
            failures.append({"id": asset_id, "name": row["name"], "error": str(error), "errorType": type(error).__name__})
        finally:
            documents = clip = actual = expected = None
            gc.collect()
        print(json.dumps({"asset": row["name"], "checked": len(checks), "failures": len(failures)}, ensure_ascii=False), flush=True)
    result = {"sourceFilter": source_filter, "fileChecksPassed": bool(checks) and not failures,
              "allComparisonsAvailable": not incomplete, "checks": checks, "failures": failures,
              "incomplete": incomplete,
              "scope": "Independent YAML parsing and ANIM/curves-JSON consistency; not Unity runtime import or complete model binding."}
    report = evidence / ("animation-yaml-validation-" + hashlib.sha256(source_filter.encode()).hexdigest()[:16] + ".json")
    report.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({**result, "checks": len(checks), "report": str(report)}, ensure_ascii=False))
    return 1 if failures or not checks else 0


if __name__ == "__main__":
    raise SystemExit(main())
