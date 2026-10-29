using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private async Task VerifyWaitingRoomPortraits(RoomWaitingPanel waiting, WorldSnapshot snapshot, int localId)
    {
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("SALA DE ESPERA: " + message); }
        async Task Frames()
        { for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        EquipmentAvatar Portrait(int id) => (EquipmentAvatar)waiting.FindChild("WaitingPortrait" + id, true, false);
        var mythic = new EquipmentAppearance(ItemTier.Mythic, ItemTier.Mythic, ItemTier.Mythic, ItemTier.Mythic)
        {
            GoldRing = ItemTier.Mythic, MagicRing = ItemTier.Mythic,
            MythicFamilies = (3UL << ((int)EquipmentFamily.Fire * 4)) | (3UL << ((int)EquipmentFamily.Cold * 4))
        };
        var host = snapshot.Players.Single(p => p.Id == localId) with { WizardType = WizardType.Pyromancer, Equipment = mythic };
        var rival = snapshot.Players.Single(p => p.Id != localId) with { WizardType = WizardType.Cryomancer, Equipment = null };
        snapshot = snapshot with { Players = [host, rival], LobbyReadyPlayerIds = [] };
        waiting.Update(snapshot, localId, true); await Frames();
        var portrait = Portrait(localId);
        Check(portrait.DisplayedWizardType == WizardType.Pyromancer && portrait.EquippedHaloCount == 6 &&
            portrait.MythicEyesVisible, "el anfitrión pierde su sprite, aura o sus ojos míticos.");
        Check(Portrait(rival.Id).DisplayedWizardType == WizardType.Cryomancer &&
            Portrait(rival.Id).EquippedHaloCount == 0 && !Portrait(rival.Id).MythicEyesVisible,
            "el rival usa el personaje o los halos de otro jugador.");
        Check(portrait.FindChildren("EquipmentSlot*", "Button", true, false).Count == 0,
            "el retrato contiene controles del inventario.");
        var viewport = GetViewport().GetVisibleRect().Grow(1);
        var visibleRoster = ((ScrollContainer)waiting.FindChild("WaitingRoomScroll", true, false)).GetGlobalRect().Intersection(viewport).Grow(1);
        Check(visibleRoster.Encloses(portrait.GetGlobalRect()) && visibleRoster.Encloses(Portrait(rival.Id).GetGlobalRect()),
            "los dos retratos iniciales quedan fuera del área visible del listado.");
        ulong instance = portrait.GetInstanceId();
        snapshot = snapshot with { LobbyReadyPlayerIds = [localId, rival.Id] };
        waiting.Update(snapshot, localId, true); await Frames();
        Check(Portrait(localId).GetInstanceId() == instance &&
            !((Button)waiting.FindChild("WaitingRoomStart", true, false)).Disabled &&
            ((Button)waiting.FindChild("WaitingRoomReady", true, false)).Text == "Cancelar preparado",
            "prepararse reinicia el retrato o no habilita el comienzo.");
        waiting.Hide();
        Check(!portrait.IsProcessing(), "el retrato sigue procesando partículas ocultas.");
        waiting.Show();
        Check(portrait.IsProcessing(), "las partículas no vuelven al mostrar la sala.");

        // El aspecto puede llegar después de la identidad: el mismo nodo debe refrescarse.
        host = host with { WizardType = WizardType.Geomancer, Equipment = new EquipmentAppearance(ItemTier.Silver, ItemTier.Gold) };
        snapshot = snapshot with { Players = [host, rival] };
        waiting.Update(snapshot, localId, true);
        Check(Portrait(localId).GetInstanceId() == instance && portrait.DisplayedWizardType == WizardType.Geomancer &&
            portrait.EquippedHaloCount == 2 && !portrait.MythicEyesVisible,
            "cambiar tipo o equipo conserva un aspecto obsoleto.");
        rival = rival with { Team = 0 };
        snapshot = snapshot with { Players = [host, rival], LobbyHostId = rival.Id };
        waiting.Update(snapshot, localId, true); await Frames();
        Check(waiting.FindChild("WaitingRedTeam", true, false).FindChild("WaitingPlayer" + rival.Id, true, false) == null &&
            waiting.FindChild("WaitingBlueTeam", true, false).FindChild("WaitingPlayer" + rival.Id, true, false) != null &&
            !((Button)waiting.FindChild("WaitingRoomStart", true, false)).Visible,
            "cambiar de equipo o de anfitrión deja tarjetas o acciones antiguas.");

        var party = Enumerable.Range(0, 8).Select(i => host with { Id = 100 + i, Team = i % 2,
            Name = "Participante de prueba " + i, WizardType = (WizardType)(i % 5),
            Equipment = i % 2 == 0 ? mythic : null }).ToArray();
        waiting.Update(snapshot with { Players = party, LobbyHostId = party[0].Id,
            LobbyReadyPlayerIds = party.Select(p => p.Id).ToArray() }, party[0].Id, true);
        await Frames();
        Check(waiting.FindChildren("WaitingPortrait*", "Control", true, false).OfType<EquipmentAvatar>().Count() == 8,
            "faltan retratos en una sala de cuatro contra cuatro.");
        var scroll = (ScrollContainer)waiting.FindChild("WaitingRoomScroll", true, false);
        scroll.ScrollVertical = int.MaxValue; await Frames();
        foreach (string name in new[] { "WaitingRoomReady", "WaitingRoomStart", "WaitingRoomExit" })
            Check(viewport.Encloses(((Control)waiting.FindChild(name, true, false)).GetGlobalRect()),
                "una sala llena desplaza las acciones fuera de pantalla: " + name);
        Check(scroll.GetHScrollBar().MaxValue <= scroll.GetHScrollBar().Page + 1,
            "las tarjetas provocan desbordamiento horizontal.");
        GD.Print("WAITING_ROOM_PORTRAITS_OK: tipos, halos, ojos, actualización, preparación, equipos y ocho jugadores.");
    }
}
