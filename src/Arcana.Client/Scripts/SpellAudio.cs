using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Sonidos originales sintetizados: el proyecto no necesita archivos de audio externos.</summary>
public partial class SpellAudio : Node
{
    private readonly Dictionary<string, AudioStreamWav> _sounds = [];
    private readonly List<AudioStreamPlayer> _voices = [];
    private double _lastTime;
    private int _voice;
    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 6; i++) { var voice = new AudioStreamPlayer { VolumeDb = -23 }; AddChild(voice); _voices.Add(voice); }
    }
    public void PlayEvent(GameEvent e)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _lastTime < 0.055 || _voices.Count == 0) return;
        _lastTime = now;
        string key = e.Kind == "death" || e.Kind == "explosion" ? "boom" : e.Elements.FirstOrDefault().ToString();
        if (!_sounds.TryGetValue(key, out var sound)) { sound = Synthesize(key); _sounds[key] = sound; }
        var player = _voices[_voice++ % _voices.Count]; player.Stream = sound; player.Play();
    }
    public void StopAll()
    {
        foreach (var voice in _voices) if (GodotObject.IsInstanceValid(voice)) { voice.Stop(); voice.Stream = null; }
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
        bool boom = key == "boom";
        int length = (int)(rate * (boom ? 0.42 : 0.17));
        var data = new byte[length * 2]; var random = new Random(key.Length * 17);
        float frequency = key switch { "Life" => 650, "Lightning" => 1600, "Arcane" => 390, "Cold" => 950, "Shield" => 520, "Earth" => 90, "Fire" => 120, _ => 240 };
        for (int i = 0; i < length; i++)
        {
            double t = i / (double)rate, u = i / (double)length;
            double noise = random.NextDouble() * 2 - 1;
            double wave = Math.Sin(t * Math.Tau * frequency * (1 - 0.5 * u));
            double sample = (boom ? noise * 0.65 + wave * 0.3 : wave * 0.7 + noise * 0.15) * Math.Pow(1 - u, 2) * Math.Min(1, i / 90.0);
            short value = (short)(sample * 15000);
            data[i * 2] = (byte)(value & 255); data[i * 2 + 1] = (byte)(value >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = data };
    }
}
