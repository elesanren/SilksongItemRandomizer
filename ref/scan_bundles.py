import UnityPy, json, os
UnityPy.config.FALLBACK_UNITY_VERSION = "2021.3.4f1"

base = r"D:\Steam\steamapps\common\Hollow Knight Silksong\Hollow Knight Silksong_Data\StreamingAssets\aa\StandaloneWindows64"

def scan(path, label):
    print("="*20, label, "="*20)
    env = UnityPy.load(path)
    counts = {}
    names = {}
    for obj in env.objects:
        t = obj.type.name
        counts[t] = counts.get(t, 0) + 1
        if obj.type.name in ("Sprite", "Texture2D", "SpriteAtlas", "MonoBehaviour", "GameObject", "AnimationClip", "AnimatorController"):
            try:
                d = obj.read()
                nm = d.m_Name if hasattr(d, "m_Name") else getattr(d, "name", "?")
                names.setdefault(t, []).append(str(nm))
            except Exception as e:
                names.setdefault(t, []).append("ERR:"+str(e)[:40])
    print("COUNTS:", json.dumps(counts, indent=1))
    for t, ns in names.items():
        print(f"--- {t}: {len(ns)}")
        for n in ns[:40]:
            print("   ", repr(n))

scan(base + r"\atlases_assets_assets\sprites\_atlases\crest_get.spriteatlas.bundle", "crest_get.spriteatlas")
scan(base + r"\atlases_assets_assets\sprites\_atlases\crest.spriteatlas.bundle", "crest.spriteatlas")
scan(base + r"\prompts_assets_all.bundle", "prompts_assets_all")