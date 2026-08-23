import UnityPy, json, sys

root = r"D:\Steam\steamapps\common\Hollow Knight Silksong"
catalog = root + r"\Hollow Knight Silksong_Data\StreamingAssets\aa\catalog.bin"

env = UnityPy.load(catalog)
out = {}
for obj in env.objects:
    try:
        data = obj.read()
    except Exception as e:
        continue
    t = data.__class__.__name__
    if t not in out:
        out[t] = 0
    out[t] += 1
print("OBJECT TYPES:", json.dumps(out, indent=1))

for obj in env.objects:
    try:
        data = obj.read()
    except Exception:
        continue
    if data.__class__.__name__ == "AddressablesContentCatalog":
        d = data.__dict__
        keys = list(d.keys())
        print("FIELDS:", keys)
        res_to_id = d.get("m_ResourceToInternalIdMaps")
        internal_ids = d.get("m_InternalIds")
        dep_ids = d.get("m_DependencyIdLists")
        print("INTERNAL IDS COUNT:", len(internal_ids) if internal_ids else None)
        print("DEP LISTS COUNT:", len(dep_ids) if dep_ids else None)
        for i, iid in enumerate(internal_ids):
            print(f"[{i}] {iid}")
        print("===SEARCH: UI Msg Crest Evolve===")
        found = []
        for m in (res_to_id or []):
            entry = getattr(m, "m_InternalIdMap", None) or m
            for k, v in ((getattr(entry, k2, None), getattr(entry, "m_InternalId", None)) for k2 in ("m_Key", "m_InternalId")):
                pass
            ks = getattr(entry, "m_Key", None)
            vid = getattr(entry, "m_InternalId", None)
            if ks is not None:
                found.append((ks, vid))
        for k, v in found:
            if isinstance(k, str) and "UI Msg" in k:
                print("KEY:", repr(k), "->", repr(v))
        print("===SEARCH: prompts_assets_all===")
        for k, v in found:
            if isinstance(v, str) and ("prompts" in v.lower()):
                print("KEY:", repr(k), "->", repr(v))
        print("===DEP LIST for prompts bundle===")
        for i, iid in enumerate(internal_ids):
            if "prompts" in iid.lower():
                deps = dep_ids[i] if dep_ids and i < len(dep_ids) else None
                print(f"BUNDLE[{i}] {iid}")
                print("  DEPS:", deps)
        # dump full map to file
        with open(r"C:\Users\28267\AppData\Local\Temp\opencode\catalog_map.json", "w", encoding="utf-8") as f:
            json.dump([{"key": repr(k), "id": v} for k, v in found if isinstance(k, (str, int))], f, ensure_ascii=False, indent=1)
        print("MAP DUMPED:", len(found))
        break