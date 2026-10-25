using System.Text.RegularExpressions;

namespace Arcana.Client;

/// <summary>Decisión visual local. No modifica la simulación ni el número de enemigos.</summary>
internal sealed class AdaptiveSpellQuality
{
    private double _healthySeconds, _cooldownSeconds;
    internal bool LegacyOnly { get; }
    internal bool Enhanced { get; private set; }

    internal AdaptiveSpellQuality(bool mobile, string? adapter, int processors)
    {
        LegacyOnly = mobile && (processors is > 0 and <= 4 || LimitedAdapter(adapter));
        Enhanced = !LegacyOnly;
    }

    private static bool LimitedAdapter(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false; // Lo decide la medición real.
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        return Regex.IsMatch(name, @"adreno\s*(?:\(tm\)\s*)?(?:[345]\d{2}|6[01]\d)\b", options) ||
            Regex.IsMatch(name, @"mali[\s-]*(?:4\d{2}|t[678]\d{2}|g(?:31|51|52|57|71|72))\b", options) ||
            Regex.IsMatch(name, @"powervr\s*(?:sgx|ge8)|swiftshader|llvmpipe|software rasterizer", options);
    }

    internal void Observe(float fps, float frame95Milliseconds, double seconds, int targetFps = 60)
    {
        if (LegacyOnly || !float.IsFinite(fps) || !float.IsFinite(frame95Milliseconds) ||
            !double.IsFinite(seconds) || fps <= 0 || frame95Milliseconds <= 0 || seconds <= 0) return;
        seconds = Math.Min(seconds, 3); // Una pausa larga no cuenta como recuperación.
        int target = Math.Clamp(targetFps, 30, 60);
        float frameBudget = 1000f / target;
        bool slow = fps < target * .80f || frame95Milliseconds > frameBudget * 1.7f;
        if (slow)
        {
            Enhanced = false; _healthySeconds = 0; _cooldownSeconds = 12;
            return;
        }
        _cooldownSeconds = Math.Max(0, _cooldownSeconds - seconds);
        if (Enhanced) return;
        bool stable = fps >= target * .94f && frame95Milliseconds <= frameBudget * 1.25f;
        _healthySeconds = stable ? _healthySeconds + seconds : 0;
        if (_healthySeconds < 12 || _cooldownSeconds > 0) return;
        Enhanced = true; _healthySeconds = 0;
    }
}
