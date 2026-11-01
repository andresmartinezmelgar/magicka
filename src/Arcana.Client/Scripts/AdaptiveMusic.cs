using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Capas Ogg originales: mismo compás y comienzo exacto, transiciones sin reiniciar la pieza.</summary>
public partial class AdaptiveMusic : Node
{
    private AudioStreamPlayer? _player;
    private AudioStreamSynchronized? _score;
    private MusicLevels _target = new(.65f, 0, 0);
    private readonly float[] _levels = [0, 0, 0];
    private float _eventEnergy, _duckSeconds, _volume = .6f;
    private double _contextIn;
    private bool _quiet, _suspended, _playing, _respondToEvents;

    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless") return;
        _score = new AudioStreamSynchronized { StreamCount = 3 };
        string[] names = ["arcane_atmosphere", "arcane_battle", "arcane_danger"];
        for (int i = 0; i < names.Length; i++)
        {
            var stream = ResourceLoader.Load<AudioStreamOggVorbis>("res://Assets/Audio/" + names[i] + ".ogg");
            if (stream == null) { _score.Dispose(); _score = null; return; }
            stream.Loop = true;
            _score.SetSyncStream(i, stream);
            _score.SetSyncStreamVolume(i, -80);
        }
        _player = new AudioStreamPlayer { Name = "AdaptiveSoundtrack", Stream = _score, VolumeDb = -80 };
        AddChild(_player);
    }

    internal void SetVolume(float volume)
    {
        _volume = float.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : .6f;
        if (_player != null && _volume == 0) _player.StreamPaused = true;
    }

    internal void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        if (_player != null) _player.StreamPaused = suspended || _volume == 0;
    }

    internal void React(GameEvent e)
    {
        if (_suspended || _quiet || !_respondToEvents) return;
        if (e.Kind is "blink" or "blink_arrive") _duckSeconds = .65f;
        if (e.Kind is "cast" or "nova" or "explosion" or "arcane_burst" or "passive_chain")
            _eventEnergy = Math.Min(1, _eventEnergy + .22f);
    }

    internal void Tick(WorldSnapshot? state, int localId, bool playing, bool quiet, double delta)
    {
        if (_player == null || _score == null || _suspended) return;
        float dt = (float)Math.Clamp(delta, 0, .1);
        if (_quiet != quiet || _playing != playing) _contextIn = 0;
        _quiet = quiet; _playing = playing;
        _contextIn -= dt;
        if (_contextIn <= 0)
        {
            _target = AdaptiveAudioMix.Evaluate(state, localId, playing, quiet);
            _respondToEvents = playing && !quiet && state is { MatchEnded: false, WaitingForPlayers: false } &&
                state.Defense?.Paused != true && state.Players.FirstOrDefault(p => p.Id == localId) is { Alive: true };
            _contextIn = .2;
        }
        if (!_respondToEvents) _eventEnergy = 0;
        _eventEnergy = Math.Max(0, _eventEnergy - dt * .25f);
        _duckSeconds = Math.Max(0, _duckSeconds - dt);
        float duck = _duckSeconds > 0 ? .55f : 1;
        for (int i = 0; i < _levels.Length; i++)
        {
            float wanted = (i switch { 0 => _target.Atmosphere, 1 => Math.Max(_target.Battle, _eventEnergy), _ => _target.Danger }) * duck;
            float speed = wanted > _levels[i] ? 1.6f : .55f;
            _levels[i] = Mathf.Lerp(_levels[i], wanted, 1 - Mathf.Exp(-speed * dt));
            _score.SetSyncStreamVolume(i, Decibels(_levels[i]));
        }
        _player.VolumeDb = -12 + Decibels(_volume);
        if (!_player.Playing && _volume > 0) _player.Play();
        _player.StreamPaused = _volume == 0;
    }

    private static float Decibels(float value) => value < .0001f ? -80 : 20 * MathF.Log10(value);

    public override void _ExitTree()
    {
        _player?.Stop();
        if (_player != null) _player.Stream = null;
        _score?.Dispose(); _score = null;
    }
}
