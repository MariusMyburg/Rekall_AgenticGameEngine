using System.Text.Json.Nodes;
using Rekall.Age.World;

namespace Rekall.Age.GameTemplates;

public sealed class RekallAgeGameTemplateCatalog
{
    private readonly Dictionary<string, RekallAgeGameTemplate> _templates;

    private RekallAgeGameTemplateCatalog(IEnumerable<RekallAgeGameTemplate> templates)
    {
        _templates = templates.ToDictionary(template => template.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<RekallAgeGameTemplate> Templates =>
        _templates.Values.OrderBy(template => template.Id, StringComparer.Ordinal).ToArray();

    public RekallAgeGameTemplate GetRequired(string id)
    {
        var normalized = id.Trim().ToLowerInvariant();
        return _templates.TryGetValue(normalized, out var template)
            ? template
            : throw new InvalidOperationException($"Game template '{id}' is not registered.");
    }

    public static RekallAgeGameTemplateCatalog CreateDefault()
    {
        return new RekallAgeGameTemplateCatalog(
        [
            Create2D("pong", "Pong", "Two paddles, ball, score loop.", "arcade",
            [
                Entity("LeftPaddle", ["player"], Component("Rekall.PaddleController", Props(("side", "left"), ("speed", 8)))),
                Entity("RightPaddle", ["opponent"], Component("Rekall.AiPaddleController", Props(("side", "right"), ("speed", 7)))),
                Entity("Ball", ["ball"], Component("Rekall.Ball2D", Props(("speed", 9), ("bounces", true)))),
                Entity("Score", ["ui"], Component("Rekall.ScoreCounter", Props(("left", 0), ("right", 0))))
            ]),
            Create2D("breakout", "Breakout", "Paddle, ball, bricks, score loop.", "arcade",
            [
                Entity("Paddle", ["player"], Component("Rekall.PaddleController", Props(("axis", "horizontal"), ("speed", 8)))),
                Entity("Ball", ["ball"], Component("Rekall.Ball2D", Props(("speed", 7), ("bounces", true)))),
                Entity("BrickField", ["level"], Component("Rekall.BrickGrid", Props(("columns", 10), ("rows", 5)))),
                Entity("Score", ["ui"], Component("Rekall.ScoreCounter", Props(("score", 0))))
            ]),
            Create2D("asteroids", "Asteroids", "Ship, asteroids, projectiles, survival loop.", "arcade",
            [
                Entity("Ship", ["player"], Component("Rekall.ThrustShipController", Props(("turnSpeed", 180), ("thrust", 12)))),
                Entity("AsteroidSpawner", ["spawner"], Component("Rekall.AsteroidSpawner", Props(("count", 12), ("wrapWorld", true)))),
                Entity("ProjectilePool", ["weapon"], Component("Rekall.ProjectilePool", Props(("capacity", 24)))),
                Entity("Score", ["ui"], Component("Rekall.ScoreCounter", Props(("score", 0))))
            ]),
            Create2D("top-down-shooter", "Top-down Shooter", "Player, enemies, bullets, wave loop.", "action",
            [
                Entity("Player", ["player"], Component("Rekall.TopDownController", Props(("speed", 6))), Component("Rekall.Health", Props(("max", 100)))),
                Entity("EnemySpawner", ["spawner"], Component("Rekall.WaveSpawner", Props(("enemyCount", 8), ("interval", 2.0)))),
                Entity("Weapon", ["weapon"], Component("Rekall.ProjectileWeapon", Props(("fireRate", 6), ("damage", 10)))),
                Entity("Hud", ["ui"], Component("Rekall.Hud", Props(("showsHealth", true), ("showsScore", true))))
            ]),
            Create2D("platformer-2d", "2D Platformer", "Player movement, platforms, hazards, collectible loop.", "platformer",
            [
                Entity("Player", ["player"], Component("Rekall.PlatformerController2D", Props(("speed", 7), ("jumpForce", 13))), Component("Rekall.Health", Props(("max", 3)))),
                Entity("LevelGeometry", ["level"], Component("Rekall.Tilemap2D", Props(("width", 48), ("height", 18)))),
                Entity("Collectibles", ["collectible"], Component("Rekall.CollectibleSet", Props(("count", 20)))),
                Entity("Goal", ["goal"], Component("Rekall.LevelExit", Props(("requiresAllCollectibles", false))))
            ]),
            Create2D("tower-defense", "Tower Defense", "Path, waves, towers, base health loop.", "strategy",
            [
                Entity("EnemyPath", ["path"], Component("Rekall.Path2D", Props(("waypoints", 6)))),
                Entity("WaveDirector", ["spawner"], Component("Rekall.WaveSpawner", Props(("enemyCount", 12), ("interval", 1.5)))),
                Entity("TowerGrid", ["build"], Component("Rekall.TowerBuildGrid", Props(("columns", 12), ("rows", 8)))),
                Entity("Base", ["base"], Component("Rekall.Health", Props(("max", 20))))
            ]),
            Create2D("visual-novel", "Visual Novel Adventure", "Dialogue, choices, backgrounds, scene loop.", "narrative", ["ui", "audio"],
            [
                Entity("DialogueDirector", ["dialogue"], Component("Rekall.DialogueGraph", Props(("startNode", "intro")))),
                Entity("ChoicePanel", ["ui"], Component("Rekall.ChoicePanel", Props(("maxChoices", 4)))),
                Entity("Background", ["art"], Component("Rekall.SpriteRenderer", Props(("sprite", "background_placeholder")))),
                Entity("Music", ["audio"], Component("Rekall.AudioEmitter", Props(("loop", true))))
            ]),
            Create3D("first-person-exploration", "First-person Exploration", "First-person controller, interactables, objective loop.", "exploration",
            [
                Entity("Player", ["player"], Component("Rekall.FirstPersonController", Props(("walkSpeed", 5), ("lookSensitivity", 0.8)))),
                Entity("Environment", ["level"], Component("Rekall.MeshSet", Props(("mesh", "blockout_room")))),
                Entity("Interactables", ["interactable"], Component("Rekall.InteractableSet", Props(("count", 5)))),
                Entity("Objective", ["goal"], Component("Rekall.ObjectiveTracker", Props(("objective", "Explore the room"))))
            ]),
            Create3D("collectathon-3d", "3D Collectathon", "Third-person player, collectibles, goals, camera loop.", "collectathon",
            [
                Entity("Player", ["player"], Component("Rekall.ThirdPersonController", Props(("speed", 6), ("jumpForce", 8)))),
                Entity("FollowCameraRig", ["camera_rig"], Component("Rekall.FollowCamera", Props(("target", "Player")))),
                Entity("Collectibles", ["collectible"], Component("Rekall.CollectibleSet", Props(("count", 30)))),
                Entity("GoalGate", ["goal"], Component("Rekall.LevelExit", Props(("requiredCollectibles", 20))))
            ]),
            Create2D("puzzle", "Puzzle Game", "Grid, pieces, goals, move-count loop.", "puzzle",
            [
                Entity("PuzzleGrid", ["board"], Component("Rekall.GridBoard", Props(("columns", 8), ("rows", 8)))),
                Entity("PuzzleRules", ["rules"], Component("Rekall.PuzzleRules", Props(("moveLimit", 40)))),
                Entity("Cursor", ["player"], Component("Rekall.GridCursor", Props(("wrap", false)))),
                Entity("GoalPanel", ["ui"], Component("Rekall.ObjectiveTracker", Props(("objective", "Solve the board"))))
            ])
        ]);
    }

    private static RekallAgeGameTemplate Create2D(
        string id,
        string displayName,
        string description,
        string loopKind,
        IReadOnlyList<RekallAgeEntityDocument> entities)
    {
        return Create2D(id, displayName, description, loopKind, ["ui"], entities);
    }

    private static RekallAgeGameTemplate Create2D(
        string id,
        string displayName,
        string description,
        string loopKind,
        IReadOnlyList<string> extraCapabilities,
        IReadOnlyList<RekallAgeEntityDocument> entities)
    {
        var capabilities = new[] { "world", "rendering2d", "input" }
            .Concat(extraCapabilities)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(capability => capability, StringComparer.Ordinal)
            .ToArray();
        return new RekallAgeGameTemplate(
            id,
            displayName,
            description,
            capabilities,
            Base2D(loopKind).Concat(entities).ToArray());
    }

    private static RekallAgeGameTemplate Create3D(
        string id,
        string displayName,
        string description,
        string loopKind,
        IReadOnlyList<RekallAgeEntityDocument> entities)
    {
        return new RekallAgeGameTemplate(
            id,
            displayName,
            description,
            ["input", "rendering3d", "ui", "world"],
            Base3D(loopKind).Concat(entities).ToArray());
    }

    private static IReadOnlyList<RekallAgeEntityDocument> Base2D(string loopKind)
    {
        return
        [
            Entity("MainCamera", ["camera"], Component("Rekall.Camera2D", Props(("active", true), ("clearColor", "#102030")))),
            Entity("GameRules", ["game_rules"], Component("Rekall.PlayableLoop", Props(("kind", loopKind), ("state", "ready"))))
        ];
    }

    private static IReadOnlyList<RekallAgeEntityDocument> Base3D(string loopKind)
    {
        return
        [
            Entity("MainCamera", ["camera"], Component("Rekall.Camera3D", Props(("active", true), ("fieldOfView", 65)))),
            Entity("Sun", ["light"], Component("Rekall.DirectionalLight", Props(("intensity", 1.0)))),
            Entity("GameRules", ["game_rules"], Component("Rekall.PlayableLoop", Props(("kind", loopKind), ("state", "ready"))))
        ];
    }

    private static RekallAgeEntityDocument Entity(
        string name,
        IReadOnlyList<string> tags,
        params RekallAgeComponentDocument[] components)
    {
        var entity = RekallAgeEntityDocument.Create(name, tags)
            .AddComponent(Component("Rekall.Transform", Props(("x", 0), ("y", 0), ("z", 0))));
        foreach (var component in components)
        {
            entity = entity.AddComponent(component);
        }

        return entity;
    }

    private static RekallAgeComponentDocument Component(string type, JsonObject properties)
    {
        return RekallAgeComponentDocument.Create(type, properties);
    }

    private static JsonObject Props(params (string Key, object? Value)[] values)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in values)
        {
            obj[key] = JsonValue.Create(value);
        }

        return obj;
    }
}
