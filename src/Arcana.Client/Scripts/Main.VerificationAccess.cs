using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    internal sealed class VerificationAccess(Main owner)
    {
        internal ArenaRenderer Renderer { get => owner._renderer; }
        internal Control UiRoot { get => owner._ui; }
        internal Control Help { get => owner._help; }
        internal Control Pause { get => owner._pause; }
        internal Launcher Launcher { get => owner._launcher; }
        internal TabContainer HelpPages { get => owner._helpPages; }
        internal SpellBook SpellBook { get => owner._spellBook; }
        internal Hud? Hud { get => owner._hud; }
        internal ArenaConnection? Connection { get => owner._connection; }
        internal ArenaConnection? PendingConnection { get => owner._pendingConnection; }
        internal GameWorld? Practice { get => owner._practice; set => owner._practice = value; }
        internal PracticeSession? PracticeSession => owner._practiceSession;
        internal WorldSnapshot? Snapshot { get => owner._snapshot; set => owner._snapshot = value; }
        internal int LocalId { get => owner._localId; set => owner._localId = value; }
        internal long LocalSequence { get => owner._localSequence; set => owner._localSequence = value; }
        internal bool Playing { get => owner._playing; set => owner._playing = value; }
        internal bool Loading => owner.Loading;
        internal double LoadingProgress => owner._loadingScreen?.Progress ?? 100;
        internal long DrawnFrames => Volatile.Read(ref owner._drawnFrames);
        internal int LoadingStep => owner._loadingIndex;
        internal bool Paused { get => owner._paused; set => owner._paused = value; }
        internal bool MoveTo { get => owner._moveTo; set => owner._moveTo = value; }
        internal bool IgnoreLeftUntilRelease { get => owner._ignoreLeftUntilRelease; set => owner._ignoreLeftUntilRelease = value; }
        internal bool LocalWasAlive { get => owner._localWasAlive; set => owner._localWasAlive = value; }
        internal int LocalDeaths { get => owner._localDeaths; set => owner._localDeaths = value; }
        internal V2 Aim { get => owner._aim; set => owner._aim = value; }
        internal V2 Target { get => owner._target; set => owner._target = value; }
        internal string ServerName { get => owner._serverName; set => owner._serverName = value; }
        internal MobileControls? Touch { get => owner._touch; }
        internal AimMarker AimMarker => owner._aimMarker;
        internal EngineLogBridge EngineLog { get => owner._engineLog; }
        internal void StartGame(WorldSnapshot snapshot) => owner.ActivateGame(snapshot);
        internal void StartGameLoading(WorldSnapshot snapshot) => owner.StartGame(snapshot);
        internal void StartPractice(string name, int team, GameMode mode, int bots, ArenaMapId map = ArenaMapId.Classic) => owner.StartPractice(name, team, mode, bots, map: map);
        internal Task ConnectOnline(string address, string name, int team, string password, string room) => owner.ConnectOnline(address, name, team, password, room);
        internal void CancelPendingConnection() => owner.CancelPendingConnection();
        internal void ReturnToLauncher(string error = "") => owner.ReturnToLauncher(error);
        internal bool Post(Action action) => owner.Post(action);
        internal void DrainPosted() { while (owner._posted.TryDequeue(out var action)) action(); }
        internal void ToggleHelp() => owner.ToggleHelp();
        internal void TogglePause() => owner.TogglePause();
        internal void UpdateTouchAim() => owner.UpdateTouchAim();
        internal void UpdateAimMarker(WorldSnapshot world) => owner.UpdateAimMarker(world);
        internal void UpdateMouseMovement(bool left, bool shift) => owner.UpdateMouseMovement(left, shift);
        internal void UseMagick(int slot) => owner.UseMagick(slot);
        internal void Submit(ClientMessage message) => owner.Submit(message);
        internal ClientMessage InputMessage() => owner.InputMessage();
        internal void Notify(int what) => owner._Notification(what);
        internal void UnhandledInput(InputEvent input) => owner._UnhandledInput(input);
        internal void ExportDiagnostics() => owner.ExportDiagnostics();
    }
}
