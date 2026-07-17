using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace SilksongItemRandomizer;

public enum TrapPersonality { Friendly, Playful, Sinister }

public enum MoveStyle
{
    SurfacePatrol,
    ZigzagLoop,
    NeedleSwing,
    Spin,
    Pulse,
    Flicker,
    Jitter,
    Bounce
}

public class TrapMover : MonoBehaviour
{
    public MoveStyle style;
    public TrapPersonality personality;
    public float baseSpeed = 1f;

    public float SwingMinAngle { get; set; } = -30f;
    public float SwingMaxAngle { get; set; } = 30f;

    private float _currentSpeed;
    private int _cycleCount;
    private Vector2 _startPos;
    private Vector2 _leftEdge, _rightEdge;
    private int _moveDir = 1;
    private List<Vector2> _path;
    private int _pathIndex;
    private float _pathProgress;
    private float _swingTimer;
    private float _startAngle;
    private float _spinAngle;
    private int _spinDir = 1;
    private Vector3 _originalScale;
    private float _pulseTimer;
    private SpriteRenderer _spriteRenderer;
    private float _flickerTimer;
    private bool _isVisible = true;
    private Vector3 _originalPos;
    private float _jitterTimer;
    private float _bounceTimer;

    private void Start()
    {
        _startPos = transform.position;
        _startAngle = transform.eulerAngles.z;
        _originalScale = transform.localScale;
        _originalPos = transform.position;
        _spriteRenderer = GetComponent<SpriteRenderer>();

        switch (style)
        {
            case MoveStyle.SurfacePatrol:
                (_leftEdge, _rightEdge) = ScanPlatformEdges(_startPos);
                break;
            case MoveStyle.ZigzagLoop:
                if (UnityEngine.Random.value < 0.5f)
                    _path = GeneratePresetPath(_startPos);
                else
                    _path = GenerateZigzagLoop(_startPos, 5, 10, 8f, 6f);
                break;
            case MoveStyle.Spin:
                _spinDir = UnityEngine.Random.Range(0, 2) == 0 ? 1 : -1;
                break;
            case MoveStyle.Pulse:
                _pulseTimer = UnityEngine.Random.Range(0f, 2f);
                break;
            case MoveStyle.Flicker:
                _flickerTimer = UnityEngine.Random.Range(0f, 3f);
                break;
            case MoveStyle.Jitter:
                _jitterTimer = UnityEngine.Random.Range(0f, 1f);
                break;
            case MoveStyle.Bounce:
                _bounceTimer = UnityEngine.Random.Range(0f, Mathf.PI * 2);
                break;
        }
        UpdateSpeed();
    }

    private void Update()
    {
        UpdateSpeed();
        switch (style)
        {
            case MoveStyle.SurfacePatrol: DoSurfacePatrol(); break;
            case MoveStyle.ZigzagLoop: DoZigzagLoop(); break;
            case MoveStyle.NeedleSwing: DoNeedleSwing(); break;
            case MoveStyle.Spin: DoSpin(); break;
            case MoveStyle.Pulse: DoPulse(); break;
            case MoveStyle.Flicker: DoFlicker(); break;
            case MoveStyle.Jitter: DoJitter(); break;
            case MoveStyle.Bounce: DoBounce(); break;
        }
    }

    private void UpdateSpeed()
    {
        _currentSpeed = personality switch
        {
            TrapPersonality.Friendly => baseSpeed * (0.15f + UnityEngine.Random.value * 0.1f),
            TrapPersonality.Playful => baseSpeed * (0.6f + UnityEngine.Random.value * 0.4f),
            TrapPersonality.Sinister => UpdateSinisterSpeed(),
            _ => _currentSpeed
        };
    }

    private float UpdateSinisterSpeed()
    {
        _cycleCount++;
        if (_cycleCount >= 180)
        {
            _cycleCount = 0;
            return baseSpeed * (UnityEngine.Random.value > 0.5f ? 0.75f : 0.15f);
        }
        return _currentSpeed;
    }

    private void DoSurfacePatrol()
    {
        var target = _moveDir > 0 ? _rightEdge : _leftEdge;
        transform.position = Vector2.MoveTowards(transform.position, target, _currentSpeed * Time.deltaTime);
        if (Vector2.Distance(transform.position, target) < 0.1f) _moveDir *= -1;
    }

    private (Vector2 left, Vector2 right) ScanPlatformEdges(Vector2 origin)
    {
        var mask = LayerMask.GetMask("Terrain");
        var y = origin.y + 0.5f;
        var left = origin.x;
        var right = origin.x;

        for (var i = 0; i < 40; i++)
        {
            if (Physics2D.Raycast(new Vector2(left - 0.5f, y), Vector2.down, 1f, mask))
                left -= 0.5f;
            else break;
        }
        for (var i = 0; i < 40; i++)
        {
            if (Physics2D.Raycast(new Vector2(right + 0.5f, y), Vector2.down, 1f, mask))
                right += 0.5f;
            else break;
        }
        return (new Vector2(left, origin.y), new Vector2(right, origin.y));
    }

    private void DoZigzagLoop()
    {
        if (_path == null || _path.Count < 2) return;
        _pathProgress += _currentSpeed * Time.deltaTime;
        while (_pathProgress > 1f && _pathIndex < _path.Count - 1)
        {
            _pathProgress -= 1f;
            _pathIndex++;
        }
        if (_pathIndex >= _path.Count - 1)
        {
            _pathIndex = 0;
            _pathProgress = 0f;
        }
        transform.position = Vector2.Lerp(_path[_pathIndex], _path[_pathIndex + 1], _pathProgress);
    }

    private void DoNeedleSwing()
    {
        _swingTimer += Time.deltaTime * _currentSpeed;
        var angle = Mathf.Lerp(SwingMinAngle, SwingMaxAngle, (Mathf.Sin(_swingTimer) + 1f) / 2f);
        transform.rotation = Quaternion.Euler(0, 0, _startAngle + angle);
    }

    private void DoSpin()
    {
        _spinAngle += 90f * _currentSpeed * Time.deltaTime * _spinDir;
        transform.rotation = Quaternion.Euler(0, 0, _spinAngle);
    }

    private void DoPulse()
    {
        _pulseTimer += Time.deltaTime * _currentSpeed * 2f;
        var scale = 1f + Mathf.Sin(_pulseTimer) * 0.2f;
        transform.localScale = _originalScale * scale;
    }

    private void DoFlicker()
    {
        _flickerTimer += Time.deltaTime * _currentSpeed;
        var period = 0.5f;
        var shouldBeVisible = (Mathf.FloorToInt(_flickerTimer / period) % 2) == 0;
        if (shouldBeVisible == _isVisible) return;
        _isVisible = shouldBeVisible;
        if (_spriteRenderer != null)
            _spriteRenderer.enabled = _isVisible;
        else
            gameObject.SetActive(_isVisible);
    }

    private void DoJitter()
    {
        _jitterTimer += Time.deltaTime * 10f;
        var offsetX = (Mathf.PerlinNoise(_jitterTimer, 0) - 0.5f) * 0.2f;
        var offsetY = (Mathf.PerlinNoise(0, _jitterTimer) - 0.5f) * 0.2f;
        transform.position = _originalPos + new Vector3(offsetX, offsetY, 0);
    }

    private void DoBounce()
    {
        _bounceTimer += Time.deltaTime * _currentSpeed * 2f;
        var offsetY = Mathf.Sin(_bounceTimer) * 0.3f;
        transform.position = new Vector3(_originalPos.x, _originalPos.y + offsetY, _originalPos.z);
    }

    // ----- 路径生成（静态方法，无状态）-----
    private static List<Vector2> GeneratePresetPath(Vector2 origin)
    {
        const float radius = 4f;
        var shapes = new List<System.Func<List<Vector2>>>
        {
            () => Square(origin, radius),
            () => Hexagon(origin, radius),
            () => Hexagram(origin, radius),
            () => Triangle(origin, radius),
            () => Pentagram(origin, radius),
            () => Circle(origin, radius, 14),
            () => Figure8(origin, radius, 12),
            () => Diamond(origin, radius),
            () => Cross(origin, radius),
        };
        return shapes[UnityEngine.Random.Range(0, shapes.Count)]();
    }

    private static List<Vector2> Square(Vector2 o, float r) =>
        new() { o + new Vector2(-r, r), o + new Vector2(r, r), o + new Vector2(r, -r), o + new Vector2(-r, -r), o + new Vector2(-r, r), o };

    private static List<Vector2> Hexagon(Vector2 o, float r)
    {
        var pts = new List<Vector2>();
        for (var i = 0; i <= 6; i++)
        {
            var angle = Mathf.Deg2Rad * (60f * i - 30f);
            pts.Add(o + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r));
        }
        pts.Add(o);
        return pts;
    }

    private static List<Vector2> Hexagram(Vector2 o, float r)
    {
        var pts = new List<Vector2>();
        for (var i = 0; i < 6; i++)
        {
            var angle = Mathf.Deg2Rad * (60f * i - 90f);
            pts.Add(o + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r));
        }
        pts.Add(o);
        return pts;
    }

    private static List<Vector2> Triangle(Vector2 o, float r)
    {
        var pts = new List<Vector2>();
        for (var i = 0; i < 3; i++)
        {
            var angle = Mathf.Deg2Rad * (120f * i - 90f);
            pts.Add(o + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r));
        }
        pts.Add(o);
        return pts;
    }

    private static List<Vector2> Pentagram(Vector2 o, float r)
    {
        var pts = new List<Vector2>();
        var r2 = r * 0.382f;
        for (var i = 0; i < 5; i++)
        {
            var angle = Mathf.Deg2Rad * (72f * i - 90f);
            pts.Add(o + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r));
            pts.Add(o + new Vector2(Mathf.Cos(angle + Mathf.Deg2Rad * 36f) * r2, Mathf.Sin(angle + Mathf.Deg2Rad * 36f) * r2));
        }
        pts.Add(o);
        return pts;
    }

    private static List<Vector2> Circle(Vector2 o, float r, int segments)
    {
        var pts = new List<Vector2>();
        for (var i = 0; i <= segments; i++)
        {
            var angle = Mathf.Deg2Rad * (360f * i / segments);
            pts.Add(o + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r));
        }
        pts.Add(o);
        return pts;
    }

    private static List<Vector2> Figure8(Vector2 o, float r, int segments)
    {
        var pts = new List<Vector2>();
        for (var i = 0; i <= segments / 2; i++)
        {
            var angle = Mathf.Deg2Rad * (360f * i / segments);
            pts.Add(o + new Vector2(r + Mathf.Cos(angle) * r, Mathf.Sin(angle) * r * 1.5f));
        }
        for (var i = segments / 2; i <= segments; i++)
        {
            var angle = Mathf.Deg2Rad * (360f * i / segments);
            pts.Add(o + new Vector2(-r + Mathf.Cos(angle) * r, Mathf.Sin(angle) * r * 1.5f));
        }
        pts.Add(o);
        return pts;
    }

    private static List<Vector2> Diamond(Vector2 o, float r) =>
        new() { o + new Vector2(0, r), o + new Vector2(r, 0), o + new Vector2(0, -r), o + new Vector2(-r, 0), o + new Vector2(0, r), o };

    private static List<Vector2> Cross(Vector2 o, float r)
    {
        var half = r * 0.4f;
        return new List<Vector2>
        {
            o + new Vector2(0, r),
            o + new Vector2(half, r * 0.6f),
            o + new Vector2(half, half),
            o + new Vector2(r * 0.6f, half),
            o + new Vector2(r, 0),
            o + new Vector2(r * 0.6f, -half),
            o + new Vector2(half, -half),
            o + new Vector2(half, -r * 0.6f),
            o + new Vector2(0, -r),
            o + new Vector2(-half, -r * 0.6f),
            o + new Vector2(-half, -half),
            o + new Vector2(-r * 0.6f, -half),
            o + new Vector2(-r, 0),
            o + new Vector2(-r * 0.6f, half),
            o + new Vector2(-half, half),
            o + new Vector2(-half, r * 0.6f),
            o
        };
    }

    private static List<Vector2> GenerateZigzagLoop(Vector2 start, int minSeg, int maxSeg, float maxW, float maxH)
    {
        var rng = new System.Random();
        var n = rng.Next(minSeg, maxSeg);
        var list = new List<Vector2> { start };
        var x = start.x;
        var y = start.y;
        for (var i = 0; i < n - 1; i++)
        {
            var dx = (rng.Next(2) == 0 ? 1 : -1) * (5f + (float)rng.NextDouble() * 7f);
            var dy = (rng.Next(2) == 0 ? 1 : -1) * (3f + (float)rng.NextDouble() * 5f);
            x += dx;
            y += dy;
            list.Add(new Vector2(x, y));
        }
        list.Add(start);
        return list;
    }
}

public static class TrapMovement
{
    private const float GlobalMoveChance = 0.4f;

    private static readonly List<MoveStyle> AllStyles = new()
    {
        MoveStyle.SurfacePatrol,
        MoveStyle.ZigzagLoop,
        MoveStyle.NeedleSwing,
        MoveStyle.Spin,
        MoveStyle.Pulse,
        MoveStyle.Flicker,
        MoveStyle.Jitter,
        MoveStyle.Bounce
    };

    private static readonly HashSet<string> ExcludedTraps = new() { "lava_area" };

    public static void ApplyTrapMovement(GameObject trapObj, string trapId, Random rng)
    {
        if (ExcludedTraps.Contains(trapId)) return;
        if (rng.NextDouble() > GlobalMoveChance) return;

        var style = AllStyles[rng.Next(AllStyles.Count)];
        var personality = (TrapPersonality)rng.Next(0, 3);

        var mover = trapObj.AddComponent<TrapMover>();
        mover.style = style;
        mover.personality = personality;
        mover.baseSpeed = 2f;

        if (style == MoveStyle.NeedleSwing)
        {
            mover.SwingMinAngle = -30f + (float)rng.NextDouble() * 10f - 5f;
            mover.SwingMaxAngle = 30f + (float)rng.NextDouble() * 10f - 5f;
        }
    }
}