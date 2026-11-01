using Godot;

namespace Arcana.Client;

public sealed class AudioPreferences
{
    public float Music { get; set; } = .6f;
    public float Effects { get; set; } = .85f;
    internal void Normalize()
    {
        Music = float.IsFinite(Music) ? Math.Clamp(Music, 0, 1) : .6f;
        Effects = float.IsFinite(Effects) ? Math.Clamp(Effects, 0, 1) : .85f;
    }
}

public partial class Main
{
    private AdaptiveMusic? _music;
    private AudioPreferences _audioPreferences = new();
    private event Action? AudioPreferencesChanged;

    private void InitializeAudio()
    {
        _audioPreferences = DevicePreferences.Read<AudioPreferences>("audio") ?? new();
        _audioPreferences.Normalize();
        _music = new AdaptiveMusic(); AddChild(_music);
        _music.SetVolume(_audioPreferences.Music);
        _renderer.SetEffectsVolume(_audioPreferences.Effects);
        _renderer.AudioEvent += _music.React;
    }

    private void TickAudio(double delta)
    {
        bool quiet = Loading || _reconnecting || _paused || _help.Visible || InspectingEquipment || ControlsOpen ||
            ViewingDamageSummary || DefenseInterfaceOpen || _profiler?.Running == true;
        _renderer.SetAudioQuiet(quiet);
        _music?.SetSuspended(_applicationSuspended || _profiler?.Running == true);
        _music?.Tick(_connection?.Latest ?? _snapshot, _localId, _playing, quiet, delta);
    }

    internal void BuildAudioControls(VBoxContainer parent)
    {
        var box = new VBoxContainer { Name = "AudioSettings", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 5); parent.AddChild(box);
        box.AddChild(Ui.Label("SONIDO", 18, Ui.Gold));
        var musicLabel = Ui.Label("", 16, Ui.Muted); box.AddChild(musicLabel);
        var music = new HSlider { Name = "MusicVolume", MinValue = 0, MaxValue = 100, Step = 1,
            CustomMinimumSize = new(0, Ui.IsMobile ? 40 : 28), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddChild(music);
        var effectsLabel = Ui.Label("", 16, Ui.Muted); box.AddChild(effectsLabel);
        var effects = new HSlider { Name = "EffectsVolume", MinValue = 0, MaxValue = 100, Step = 1,
            CustomMinimumSize = new(0, Ui.IsMobile ? 40 : 28), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddChild(effects);
        void Refresh()
        {
            music.SetValueNoSignal(Math.Round(_audioPreferences.Music * 100));
            effects.SetValueNoSignal(Math.Round(_audioPreferences.Effects * 100));
            musicLabel.Text = $"Música · {music.Value:0} %";
            effectsLabel.Text = $"Efectos · {effects.Value:0} %";
        }
        void Apply(bool save)
        {
            _music?.SetVolume(_audioPreferences.Music);
            _renderer.SetEffectsVolume(_audioPreferences.Effects);
            AudioPreferencesChanged?.Invoke();
            if (save) DevicePreferences.Write("audio", _audioPreferences);
        }
        music.ValueChanged += value => { _audioPreferences.Music = (float)value / 100; Apply(false); };
        effects.ValueChanged += value => { _audioPreferences.Effects = (float)value / 100; Apply(false); };
        // Guardar al soltar evita escrituras de almacenamiento en cada paso del arrastre.
        music.DragEnded += _ => Apply(true);
        effects.DragEnded += _ => Apply(true);
        music.FocusExited += () => Apply(true);
        effects.FocusExited += () => Apply(true);
        AudioPreferencesChanged += Refresh;
        box.TreeExiting += () => { AudioPreferencesChanged -= Refresh; DevicePreferences.Write("audio", _audioPreferences); };
        Refresh();
    }
}
