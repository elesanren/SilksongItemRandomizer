using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 全局 Sprite 缓存：将 Resources.FindObjectsOfTypeAll&lt;Sprite&gt;() 的扫描结果缓存到 Dictionary，
    /// 避免每次查找图标时遍历数万个资源对象。
    /// </summary>
    public static class SpriteCache
    {
        private static Dictionary<string, Sprite> _cache;
        private static bool _built = false;

        public static void EnsureBuilt()
        {
            if (_built && _cache != null) return;
            var all = Resources.FindObjectsOfTypeAll<Sprite>();
            _cache = new Dictionary<string, Sprite>(all.Length);
            foreach (var s in all)
            {
                if (s == null || string.IsNullOrEmpty(s.name)) continue;
                _cache[s.name] = s;  // 同名取第一个
            }
            _built = true;
            Plugin.Log.LogInfo($"[SpriteCache] 已缓存 {_cache.Count} 个 Sprite");
        }

        public static Sprite Find(string name)
        {
            EnsureBuilt();
            _cache.TryGetValue(name, out var sprite);
            return sprite;
        }

        /// <summary>返回当前缓存的全部 Sprite 名单（同名已去重），供全量图标搜索使用。</summary>
        public static IEnumerable<Sprite> GetAll()
        {
            EnsureBuilt();
            return _cache.Values;
        }

        public static void Reset()
        {
            _cache = null;
            _built = false;
        }
    }
}
