import re, json

path = r"D:\Steam\steamapps\common\Hollow Knight Silksong\Hollow Knight Silksong_Data\StreamingAssets\aa\catalog.bin"
data = open(path, "rb").read()
print("size:", len(data))

strs = re.findall(rb"[\x20-\x7e]{4,}", data)
dec = [s.decode() for s in strs]
print("total strings:", len(dec))

hits_ui = [s for s in dec if "UI Msg" in s]
print("=== UI Msg hits ===")
for s in hits_ui[:50]:
    print(repr(s))

hits_prefab = [s for s in dec if s.endswith(".prefab") or "Prefabs" in s]
print("=== prefab-ish hits", len(hits_prefab))
for s in hits_prefab[:80]:
    print(repr(s))

hits_prompt = [s for s in dec if "prompt" in s.lower()]
print("=== prompts hits", len(hits_prompt))
for s in hits_prompt[:60]:
    print(repr(s))

bundle_like = sorted(set(s for s in dec if s.endswith(".bundle") or s.endswith(".bundle/")))
print("=== bundle paths", len(bundle_like))
for s in bundle_like[:200]:
    print(repr(s))

hits_crest = [s for s in dec if "crest" in s.lower()]
print("=== crest hits ===")
for s in hits_crest[:100]:
    print(repr(s))

with open(r"C:\Users\28267\AppData\Local\Temp\opencode\catalog_strings.json", "w", encoding="utf-8") as f:
    json.dump(dec, f)