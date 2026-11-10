namespace Arcana.Core;

public enum TutorialChapter { Basics, Duel, Arena, Warfare, Survival, Inventory, Completed }

public sealed partial class GameWorld
{
    private bool _tutorialFirstWave;

    /// <summary>Partidas locales preparadas para practicar con las reglas de combate y equipo del juego.</summary>
    public sealed class TutorialScenario
    {
        public TutorialChapter Chapter { get; }
        public GameWorld World { get; }
        public Wizard Player { get; }
        public Wizard? Target { get; private set; }
        public string TrainingWeaponId { get; } = "";
        public static V2 MovementStart => new(-7, 0);
        public static V2 MovementGoal => new(-2, 0);

        public TutorialScenario(TutorialChapter chapter, string name)
        {
            Chapter = chapter;
            var mode = chapter switch
            {
                TutorialChapter.Basics or TutorialChapter.Arena => GameMode.Arena,
                TutorialChapter.Duel => GameMode.Duel,
                TutorialChapter.Warfare => GameMode.Warfare,
                TutorialChapter.Survival or TutorialChapter.Inventory => GameMode.Defense,
                _ => throw new ArgumentOutOfRangeException(nameof(chapter))
            };
            bool defense = mode == GameMode.Defense;
            World = new(new WorldOptions
            {
                Mode = mode, Map = defense ? ArenaMapId.Bastion : ArenaMapId.Classic,
                SurvivalMap = defense ? SurvivalMapSpec.Legacy : null,
                MaxPlayers = chapter == TutorialChapter.Arena ? 3 : 2,
                ScoreLimit = chapter == TutorialChapter.Duel ? 1 : chapter == TutorialChapter.Arena ? 2 : 15,
                Tickets = 75, FriendlyFire = false, Seed = 73
            });
            Player = World.AddPlayer(name, 0)!;
            Player.Invulnerable = 0;
            switch (chapter)
            {
                case TutorialChapter.Basics:
                    Target = World.AddPlayer("Objetivo de práctica", 1)!;
                    ResetExercise();
                    Player.Position = MovementStart;
                    break;
                case TutorialChapter.Duel:
                    Player.Position = new(-3, 0);
                    Target = Rival("Rival de duelo", new(3, 0));
                    break;
                case TutorialChapter.Arena:
                    Player.Position = new(-3, 0);
                    Target = Rival("Rival 1", new(2, -3));
                    Rival("Rival 2", new(2, 3));
                    break;
                case TutorialChapter.Warfare:
                    Player.Position = new(-5, 0);
                    break;
                case TutorialChapter.Survival:
                    World._tutorialFirstWave = true;
                    PrepareFirstWave();
                    break;
                case TutorialChapter.Inventory:
                    World._spawnClock = 180;
                    var random = new Random(73);
                    foreach (var slot in Enum.GetValues<EquipmentSlot>())
                        World.GrantDefenseItem(Player, EquipmentCatalogue.Generate(random, tier: ItemTier.Silver, slot: slot));
                    var weapon = EquipmentCatalogue.Generate(random, tier: ItemTier.Gold, slot: EquipmentSlot.Weapon) with
                    {
                        Bonuses = [new(EquipmentStat.FirePower, 0.18f, 0.3f, 0.18f)]
                    };
                    TrainingWeaponId = weapon.Id;
                    World.GrantDefenseItem(Player, weapon);
                    World._defenders[Player.Id].Favorites.Add(weapon.Id);
                    break;
            }
        }

        private Wizard Rival(string name, V2 position)
        {
            var rival = World.AddPlayer(name, 1)!;
            rival.Position = position; rival.Health = rival.MaxHealth = 240; rival.Invulnerable = 0;
            return rival;
        }

        public void PrepareFirstWave()
        {
            if (Chapter != TutorialChapter.Survival) return;
            if (Target != null && World._minions.TryGetValue(Target.Id, out var previous)) World.RetireDefenseMinion(previous);
            World._projectiles.Clear(); World._fields.Clear(); World._walls.Clear(); World._beams.Clear();
            Player.Position = World.Map.Spawn(0, Player.Id % 4);
            World.Respawn(Player);
            Player.Invulnerable = 0;
            var start = Player.Position;
            // El mapa de práctica es fijo y la sala central deja cuatro trayectorias cortas para el ejercicio.
            V2[] directions = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];
            var position = directions.Select(direction => start + direction * 4).First(at => World.Map.ClearPath(start, at));
            var enemy = World.SpawnDefenseMinion(MinionKind.Melee, position);
            enemy.Attack = 1;
            Target = enemy.Body;
        }

        public void ResetExercise(bool injured = false)
        {
            if (Chapter != TutorialChapter.Basics || Target is not { } target) return;
            World.ResetMatch();
            World._events.Clear(); World._beams.Clear();
            Player.Position = MovementGoal; Player.Facing = new(1, 0); Player.Invulnerable = 0;
            target.Position = new(3, 0); target.Invulnerable = 0;
            if (injured) Player.Health = Player.MaxHealth / 2;
        }
    }
}
