namespace Arcana.Core;

public sealed partial class GameWorld
{
    private DefensePerformanceBudget _simulationBudget = new(DefenseRules.MaxMinions);
    private double _simulationCost;
    private int _simulationSamples;
    /// <summary>Coste real de la simulación; el anfitrión lo comunica sin alterar el reloj del juego.</summary>
    public void ReportSimulationStep(double milliseconds)
    {
        if (!IsDefense || !double.IsFinite(milliseconds) || milliseconds < 0 || DefensePaused || MatchEnded) return;
        _simulationCost += milliseconds;
        if (++_simulationSamples < 300) return;
        double mean = _simulationCost / _simulationSamples;
        float equivalentFps = mean switch { < 8 => 60, < 10 => 52, < 12 => 44, < 16 => 36, < 22 => 28, < 32 => 20, _ => 15 };
        _simulationBudget.Observe(equivalentFps);
        _simulationCost = 0; _simulationSamples = 0;
        TrimDistantDefenseMinions();
    }
    /// <summary>Desconectar no cura ni concede invulnerabilidad; cancela las órdenes y la actividad anterior.</summary>
    public void SuspendPlayer(int id)
    {
        if (!_players.TryGetValue(id, out var player)) return;
        CancelCast(player); player.Selection.Clear(); player.Input = new(); player.LastSequence = 0; player.SelectionSequence = 0; player.LastInput = Time;
        if (_defenders.TryGetValue(id, out var progress)) { progress.ShopOpen = false; progress.LastActivity = -100; }
    }
}
