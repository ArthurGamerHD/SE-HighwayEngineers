#!/usr/bin/env python3
"""Checks HighwayEngineer's localization (run from anywhere; exit code 1 on errors).

* every MyTexts.<locale>.resx has exactly the keys of MyTexts.resx, with the same {n[:format]} placeholders;
* every key the code uses exists: LocHelper/MyStringId literals, {ext:loc Key} in XAML, <loc>Key</loc> doc tags,
  DisplayName_* in .sbc files; VerbKey/NounKey prefixes need their _Suffix variants;
* keys nothing uses are reported as warnings.
"""
import os, re, sys
import xml.etree.ElementTree as ET

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "HighwayEngineer")
LOC = os.path.join(ROOT, "Content", "Data", "Localization")
PH = re.compile(r"\{\d+(?::[^}]*)?\}")
# String constants that share the prefix but are not localization keys.
NOT_KEYS = {"HighwayEngineer_RoadPlanner", "HighwayEngineer_Cursor", "HighwayEngineer_Planner"}


def load(path):
    entries = {}
    for data in ET.parse(path).getroot().findall("data"):
        name = data.get("name")
        if name in entries:
            raise SystemExit("%s: duplicate key %s" % (path, name))
        entries[name] = data.findtext("value") or ""
    return entries


errors = []
base = load(os.path.join(LOC, "MyTexts.resx"))
for f in sorted(os.listdir(LOC)):
    if not (f.startswith("MyTexts.") and f.endswith(".resx")) or f == "MyTexts.resx":
        continue
    locale = load(os.path.join(LOC, f))
    for key in sorted(set(base) ^ set(locale)):
        errors.append("%s: key %s %s" % (f, key, "missing" if key in base else "not in MyTexts.resx"))
    for key in set(base) & set(locale):
        if sorted(PH.findall(base[key])) != sorted(PH.findall(locale[key])):
            errors.append("%s: placeholders of %s differ from English" % (f, key))

used, prefixes, ids = set(), set(), set()
lit = re.compile(r'"((?:DisplayName_)?HighwayEngineer_[A-Za-z0-9_]+)"')
for d, dirs, files in os.walk(ROOT):
    dirs[:] = [x for x in dirs if x not in ("bin", "obj")]
    for f in files:
        p = os.path.join(d, f)
        if f.endswith(".cs"):
            s = open(p, encoding="utf-8-sig").read()
            for k in lit.findall(s):
                (prefixes if re.search(r'(VerbKey|NounKey)\(\s*"' + k + '"', s) else used).add(k)
            used.update(re.findall(r"<loc>\s*(HighwayEngineer_\w+)\s*</loc>", s))
        elif f.endswith(".xaml"):
            used.update(re.findall(r"\{ext:loc (\w+)\}", open(p, encoding="utf-8").read()))
        elif f.endswith(".sbc"):
            sbc = open(p, encoding="utf-8").read()
            used.update(re.findall(r"<DisplayName>(DisplayName_\w+)</DisplayName>", sbc))
            # Definition ids (voxel materials, decals, the contract type) share the prefix but are not text keys.
            ids.update(re.findall(r"<(?:SubtypeId|Source)>(HighwayEngineer_\w+)</(?:SubtypeId|Source)>", sbc))
            # LCD texture definitions name their subtype in an attribute: <Id Type="LCDTextureDefinition" Subtype="..."/>.
            ids.update(re.findall(r'Subtype="(HighwayEngineer_\w+)"', sbc))
used -= NOT_KEYS
used -= ids
for p in prefixes:
    variants = [k for k in base if k.startswith(p + "_")]
    if not variants:
        errors.append("no variants for key prefix " + p)
    used.update(variants)
errors += ["key used but not in MyTexts.resx: " + k for k in sorted(used - set(base))]
for k in sorted(set(base) - used):
    print("warning: unused key " + k)
for e in errors:
    print("error: " + e)
print("%d keys, %d locales, %d errors" % (len(base), len([f for f in os.listdir(LOC) if f.endswith(".resx")]) - 1, len(errors)))
sys.exit(1 if errors else 0)
