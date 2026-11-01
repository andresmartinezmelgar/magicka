using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Efectos originales precargados, mezclas elementales y voces reservadas para avisos.</summary>
public partial class SpellAudio : Node
{
    private const int RegularVoices = 8, TotalVoices = 12;
    private readonly Dictionary<string, AudioStreamWav> _sounds = [];
    private readonly Dictionary<string, double> _lastPlayed = [];
    private readonly List<AudioStreamPlayer> _voices = [];
    private float _volume = .85f;
    private double _nextRegular;
    private int _regularVoice, _criticalVoice;
    private bool _suspended;
    internal bool Enabled { get; set; } = true;

    public override void _Ready()
    {
        if (!Enabled || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < TotalVoices; i++)
        { var voice = new AudioStreamPlayer { VolumeDb = -18 }; AddChild(voice); _voices.Add(voice); }
    }

    internal void SetVolume(float volume)
    {
        _volume = float.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : .85f;
        if (_volume == 0) StopAll();
    }

    internal void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        if (suspended) StopAll();
    }

    public void PlayEvent(GameEvent e, float distance = 0)
    {
        if (_suspended || _volume == 0 || _voices.Count == 0) return;
        var keys = AdaptiveAudioMix.SoundKeys(e);
        if (keys.Length == 0) return;
        bool critical = AdaptiveAudioMix.Critical(e.Kind);
        double now = Time.GetTicksMsec() / 1000.0;
        if (!critical && now < _nextRegular) return;
        string group = critical ? e.Kind : e.Kind + ":" + keys[0];
        if (_lastPlayed.TryGetValue(group, out double last) && now - last < (critical ? .1 : .11)) return;
        _lastPlayed[group] = now;
        if (!critical) _nextRegular = now + .055;
        float attenuation = Math.Clamp(1 - distance / 22, .15f, 1);
        float gain = attenuation * _volume / MathF.Sqrt(keys.Length);
        foreach (string key in keys) Play(key, critical, gain);
    }

    internal void PlayResult(bool victory)
    {
        if (!_suspended && _volume > 0 && _voices.Count > 0) Play(victory ? "victory" : "defeat", true, _volume);
    }

    private void Play(string key, bool critical, float gain)
    {
        // Se prepara todo durante la carga, sin síntesis ni lecturas de disco en combate.
        if (!_sounds.TryGetValue(key, out var sound)) return;
        int slot = critical ? RegularVoices + _criticalVoice++ % (TotalVoices - RegularVoices) : _regularVoice++ % RegularVoices;
        var player = _voices[slot];
        player.VolumeDb = (critical ? -13 : -18) + 20 * MathF.Log10(Math.Max(.0001f, gain));
        player.Stream = sound; player.Play();
    }

    public void StopAll()
    {
        foreach (var voice in _voices)
            if (GodotObject.IsInstanceValid(voice)) { voice.Stop(); voice.Stream = null; }
        _lastPlayed.Clear(); _nextRegular = 0;
    }

    internal void Prepare(string key)
    {
        if (_voices.Count > 0 && !_sounds.ContainsKey(key)) _sounds[key] = Synthesize(key);
    }

    public override void _ExitTree()
    {
        StopAll();
        foreach (var sound in _sounds.Values) sound.Dispose(); _sounds.Clear();
    }
    private static AudioStreamWav Synthesize(string key)
    {
        const int rate = 22050;
        string[] parts = key.Split(':');
        string family = parts[0], element = parts.Length > 1 ? parts[1] : "";
        double seconds = family switch
        { "victory" or "defeat" => 1.5, "blink" or "blink_arrive" => .55, "explosion" or "death" => .6, "nova" => .48, _ => .26 };
        int length = (int)(rate * seconds), seed = 17;
        foreach (char c in key) seed = unchecked(seed * 31 + c);
        var random = new Random(seed);
        var data = new byte[length * 2];
        double lowNoise = 0, previousNoise = 0;
        double frequency = element switch
        { "Water" => 220, "Life" => 587.33, "Shield" => 293.66, "Cold" => 880,
            "Lightning" => 1174.66, "Arcane" => 440, "Earth" => 73.42, "Fire" => 146.83, _ => 110 };
        int[] resultNotes = family == "victory" ? [74, 77, 81, 86] : [69, 65, 62];
        for (int i = 0; i < length; i++)
        {
            double t = i / (double)rate, u = t / seconds;
            double noise = random.NextDouble() * 2 - 1;
            lowNoise += (noise - lowNoise) * .09;
            double highNoise = (noise - previousNoise) * .5; previousNoise = noise;
            double phase = Math.Tau * frequency * t * (family == "impact" ? 1 - .25 * u : 1 + .06 * u);
            double wave = element switch
            {
                "Water" => .5 * Math.Sin(phase + 2.5 * Math.Sin(t * 65)) + lowNoise * 1.4,
                "Life" => .55 * Math.Sin(phase) + .22 * Math.Sin(phase * 1.5) + .12 * Math.Sin(phase * 2),
                "Shield" => .55 * Math.Sin(phase) + .25 * Math.Sin(phase * 2.76) * Math.Exp(-t * 14),
                "Cold" => .48 * Math.Sin(phase) + .24 * Math.Sin(phase * 2.01) + highNoise * .18,
                "Lightning" => highNoise * .65 + .3 * Math.Sin(phase + noise * 2) * Math.Cos(t * 180),
                "Arcane" => .55 * Math.Sin(phase + 2 * Math.Sin(phase * .5)) + .2 * Math.Sin(phase * 1.5),
                "Earth" => .6 * Math.Sin(phase * (1 - .6 * u)) + lowNoise * 1.5,
                "Fire" => lowNoise * 1.7 + noise * .15 + .2 * Math.Sin(phase) * Math.Sin(t * 47),
                _ => 0
            };
            if (family is "blink" or "blink_arrive")
            {
                double start = family == "blink" ? 180 : 1000, end = family == "blink" ? 1000 : 260;
                double sweep = Math.Tau * t * (start + (end - start) * u * .5);
                wave = .5 * Math.Sin(sweep + Math.Sin(t * 90)) + .2 * Math.Sin(sweep * 1.5) + highNoise * .15;
            }
            else if (family is "explosion" or "death" or "shield_break" or "shield_hit")
            {
                bool shield = family is "shield_break" or "shield_hit";
                wave = shield ? .5 * Math.Sin(t * Math.Tau * 420) * Math.Exp(-t * 8) + highNoise * .35
                    : .45 * Math.Sin(t * Math.Tau * (80 - 45 * u)) + lowNoise * 1.6 + noise * .16;
            }
            else if (family is "victory" or "defeat")
            {
                wave = 0;
                for (int n = 0; n < resultNotes.Length; n++)
                {
                    double age = t - n * .18;
                    if (age < 0) continue;
                    double hz = 440 * Math.Pow(2, (resultNotes[n] - 69) / 12.0);
                    wave += Math.Sin(Math.Tau * hz * age) * Math.Exp(-age * 3) * Math.Min(1, age / .008) * .25;
                }
            }
            double envelope = Math.Min(1, t / .008) * Math.Pow(Math.Max(0, 1 - u), family == "nova" ? 1.3 : 2);
            short value = (short)(Math.Clamp(wave * envelope, -.95, .95) * 24000);
            data[i * 2] = (byte)(value & 255); data[i * 2 + 1] = (byte)(value >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = data };
    }
}
