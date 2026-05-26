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
            Create2D("pong", "Pong", "Two paddles, ball, score loop.",
            [
                Entity("LeftPaddle", ["player"], TemplateComponent("pong", "PaddleController", Props(("side", "left"), ("speed", 8)))),
                Entity("RightPaddle", ["opponent"], TemplateComponent("pong", "AiPaddleController", Props(("side", "right"), ("speed", 7)))),
                Entity("Ball", ["ball"], TemplateComponent("pong", "Ball2D", Props(("speed", 9), ("bounces", true)))),
                Entity("Score", ["ui"], TemplateComponent("pong", "ScoreCounter", Props(("left", 0), ("right", 0))))
            ]),
            Create2D("breakout", "Breakout", "Paddle, ball, bricks, score loop.",
            [
                Entity("Paddle", ["player"], TemplateComponent("breakout", "PaddleController", Props(("axis", "horizontal"), ("speed", 8)))),
                Entity("Ball", ["ball"], TemplateComponent("breakout", "Ball2D", Props(("speed", 7), ("bounces", true)))),
                Entity("BrickField", ["level"], TemplateComponent("breakout", "BrickGrid", Props(("columns", 10), ("rows", 5)))),
                Entity("Score", ["ui"], TemplateComponent("breakout", "ScoreCounter", Props(("score", 0))))
            ]),
            Create2D("asteroids", "Asteroids", "Ship, asteroids, projectiles, survival loop.",
            [
                Entity("Ship", ["player"], TemplateComponent("asteroids", "ThrustShipController", Props(("turnSpeed", 180), ("thrust", 12)))),
                Entity("AsteroidSpawner", ["spawner"], TemplateComponent("asteroids", "AsteroidSpawner", Props(("count", 12), ("wrapWorld", true)))),
                Entity("ProjectilePool", ["weapon"], TemplateComponent("asteroids", "ProjectilePool", Props(("capacity", 24)))),
                Entity("Score", ["ui"], TemplateComponent("asteroids", "ScoreCounter", Props(("score", 0))))
            ]),
            Create2D("top-down-shooter", "Top-down Shooter", "Player, enemies, bullets, wave loop.",
            [
                Entity("Player", ["player"], TemplateComponent("top-down-shooter", "TopDownController", Props(("speed", 6))), TemplateComponent("top-down-shooter", "Health", Props(("max", 100)))),
                Entity("EnemySpawner", ["spawner"], TemplateComponent("top-down-shooter", "WaveSpawner", Props(("enemyCount", 8), ("interval", 2.0)))),
                Entity("Weapon", ["weapon"], TemplateComponent("top-down-shooter", "ProjectileWeapon", Props(("fireRate", 6), ("damage", 10)))),
                Entity("Hud", ["ui"], TemplateComponent("top-down-shooter", "Hud", Props(("showsHealth", true), ("showsScore", true))))
            ]),
            Create2D("platformer-2d", "2D Platformer", "Player movement, platforms, hazards, collectible loop.",
            [
                Entity("Player", ["player"], TemplateComponent("platformer-2d", "PlatformerController2D", Props(("speed", 7), ("jumpForce", 13))), TemplateComponent("platformer-2d", "Health", Props(("max", 3)))),
                Entity("LevelGeometry", ["level"], TemplateComponent("platformer-2d", "Tilemap2D", Props(("width", 48), ("height", 18)))),
                Entity("Collectibles", ["collectible"], TemplateComponent("platformer-2d", "CollectibleSet", Props(("count", 20)))),
                Entity("Goal", ["goal"], TemplateComponent("platformer-2d", "LevelExit", Props(("requiresAllCollectibles", false))))
            ]),
            Create2D("tower-defense", "Tower Defense", "Path, waves, towers, base health loop.",
            [
                Entity("EnemyPath", ["path"], TemplateComponent("tower-defense", "Path2D", Props(("waypoints", 6)))),
                Entity("WaveDirector", ["spawner"], TemplateComponent("tower-defense", "WaveSpawner", Props(("enemyCount", 12), ("interval", 1.5)))),
                Entity("TowerGrid", ["build"], TemplateComponent("tower-defense", "TowerBuildGrid", Props(("columns", 12), ("rows", 8)))),
                Entity("Base", ["base"], TemplateComponent("tower-defense", "Health", Props(("max", 20))))
            ]),
            Create2D("visual-novel", "Visual Novel Adventure", "Dialogue, choices, backgrounds, scene loop.", ["ui", "audio"],
            [
                Entity("DialogueDirector", ["dialogue"], TemplateComponent("visual-novel", "DialogueGraph", Props(("startNode", "intro")))),
                Entity("ChoicePanel", ["ui"], TemplateComponent("visual-novel", "ChoicePanel", Props(("maxChoices", 4)))),
                Entity("Background", ["art"], Component("Rekall.SpriteRenderer", Props(("sprite", "visual_novel_intro_background")))),
                Entity("Music", ["audio"], Component("Rekall.AudioEmitter", Props(("loop", true))))
            ]),
            Create3D("first-person-exploration", "First-person Exploration", "First-person controller, interactables, objective loop.",
            [
                Entity("InputActions", ["input", "controls"],
                    Component("Rekall.InputActionMap", Props(
                        ("active", true),
                        ("actions", new JsonArray
                        {
                            Action("move.x", ("positiveKey", "D"), ("negativeKey", "A")),
                            Action("move.z", ("positiveKey", "W"), ("negativeKey", "S")),
                            Action("look.x", ("mouseAxis", "x"), ("mouseScale", -0.13)),
                            Action("look.y", ("mouseAxis", "y"), ("mouseScale", 0.09)),
                            Action("turn.x", ("positiveKey", "E"), ("negativeKey", "Q")),
                            Action("move.fast", ("key", "LeftShift")),
                            Action("fire", ("button", "Left")),
                            Action("fire", ("key", "Space")),
                            Action("fire", ("key", "Enter")),
                            Action("fire", ("key", "Return"))
                        })))),
                Entity("Player", ["player"], TemplateComponent("first-person-exploration", "FirstPersonController", Props(("walkSpeed", 5), ("lookSensitivity", 0.8)))),
                Entity("Environment", ["level"], Component("Rekall.MeshSet", Props(("mesh", "blockout_room")))),
                Entity("Interactables", ["interactable"], TemplateComponent("first-person-exploration", "InteractableSet", Props(("count", 5)))),
                Entity("Objective", ["goal"], TemplateComponent("first-person-exploration", "ObjectiveTracker", Props(("objective", "Explore the room"))))
            ]),
            Create3D("collectathon-3d", "3D Collectathon", "Third-person player, collectibles, goals, camera loop.",
            [
                Entity("Player", ["player"], TemplateComponent("collectathon-3d", "ThirdPersonController", Props(("speed", 6), ("jumpForce", 8)))),
                Entity("FollowCameraRig", ["camera_rig"], TemplateComponent("collectathon-3d", "FollowCamera", Props(("target", "Player")))),
                Entity("Collectibles", ["collectible"], TemplateComponent("collectathon-3d", "CollectibleSet", Props(("count", 30)))),
                Entity("GoalGate", ["goal"], TemplateComponent("collectathon-3d", "LevelExit", Props(("requiredCollectibles", 20))))
            ]),
            Create2D("puzzle", "Puzzle Game", "Grid, pieces, goals, move-count loop.",
            [
                Entity("PuzzleGrid", ["board"], TemplateComponent("puzzle", "GridBoard", Props(("columns", 8), ("rows", 8)))),
                Entity("PuzzleRules", ["rules"], TemplateComponent("puzzle", "PuzzleRules", Props(("moveLimit", 40)))),
                Entity("Cursor", ["player"], TemplateComponent("puzzle", "GridCursor", Props(("wrap", false)))),
                Entity("GoalPanel", ["ui"], TemplateComponent("puzzle", "ObjectiveTracker", Props(("objective", "Solve the board"))))
            ])
        ]);
    }

    private static RekallAgeGameTemplate Create2D(
        string id,
        string displayName,
        string description,
        IReadOnlyList<RekallAgeEntityDocument> entities)
    {
        return Create2D(id, displayName, description, ["ui"], entities);
    }

    private static RekallAgeGameTemplate Create2D(
        string id,
        string displayName,
        string description,
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
            Base2D().Concat(entities).ToArray())
        {
            DrawCommands = DrawCommandsFor(id)
        };
    }

    private static RekallAgeGameTemplate Create3D(
        string id,
        string displayName,
        string description,
        IReadOnlyList<RekallAgeEntityDocument> entities)
    {
        return new RekallAgeGameTemplate(
            id,
            displayName,
            description,
            ["input", "rendering3d", "ui", "world"],
            Base3D().Concat(entities).ToArray())
        {
            DrawCommands = DrawCommandsFor(id)
        };
    }

    private static IReadOnlyList<RekallAgeTemplateDrawCommand> DrawCommandsFor(string id)
    {
        return id switch
        {
            "pong" =>
            [
                Draw("background", "clear", "frame clear color"),
                Draw("left-paddle", "rect", "player paddle"),
                Draw("right-paddle", "rect", "opponent paddle"),
                Draw("ball", "circle", "active ball"),
                Draw("hud", "text", "score display")
            ],
            "breakout" =>
            [
                Draw("background", "clear", "frame clear color"),
                Draw("brick-field", "rect", "remaining brick field"),
                Draw("paddle", "rect", "player paddle"),
                Draw("ball", "circle", "active ball"),
                Draw("hud", "text", "score display")
            ],
            "asteroids" =>
            [
                Draw("background", "clear", "frame clear color"),
                Draw("ship", "rect", "player ship"),
                Draw("asteroid-alpha", "circle", "representative asteroid"),
                Draw("projectile", "rect", "fired projectile"),
                Draw("hud", "text", "score display")
            ],
            "top-down-shooter" =>
            [
                Draw("background", "clear", "frame clear color"),
                Draw("player", "rect", "player avatar"),
                Draw("enemy-wave", "rect", "enemy wave band"),
                Draw("projectile", "rect", "player projectile"),
                Draw("hud", "text", "score display")
            ],
            "platformer-2d" =>
            [
                Draw("background", "clear", "frame clear color"),
                Draw("platform-ground", "rect", "ground platform"),
                Draw("runner", "rect", "player runner"),
                Draw("collectible", "circle", "collectible marker"),
                Draw("hud", "text", "score display")
            ],
            "tower-defense" =>
            [
                Draw("background", "clear", "frame clear color"),
                Draw("enemy-path", "rect", "enemy travel path"),
                Draw("tower", "rect", "placed tower"),
                Draw("enemy-wave", "circle", "incoming enemy"),
                Draw("base-health", "text", "base health display")
            ],
            "visual-novel" =>
            [
                Draw("background", "clear", "frame clear color"),
                Draw("background-panel", "rect", "scene background"),
                Draw("portrait-left", "rect", "speaker portrait"),
                Draw("dialogue-box", "rect", "dialogue container"),
                Draw("choice-cursor", "text", "choice selector")
            ],
            "first-person-exploration" =>
            [
                Draw("background", "clear", "frame clear color"),
                Draw("corridor", "rect", "first-person corridor view"),
                Draw("reticle", "rect", "look reticle"),
                Draw("interaction-hotspot", "circle", "interactable marker"),
                Draw("objective", "text", "objective tracker")
            ],
            "collectathon-3d" =>
            [
                Draw("background", "clear", "frame clear color"),
                Draw("camera-orbit", "rect", "camera framing volume"),
                Draw("avatar", "rect", "player avatar"),
                Draw("collectible", "circle", "collectible marker"),
                Draw("goal-gate", "text", "collection progress")
            ],
            "puzzle" =>
            [
                Draw("background", "clear", "frame clear color"),
                Draw("grid", "rect", "puzzle board"),
                Draw("tile-active", "rect", "active puzzle tile"),
                Draw("cursor", "rect", "board cursor"),
                Draw("objective", "text", "move counter")
            ],
            _ => []
        };
    }

    private static RekallAgeTemplateDrawCommand Draw(string id, string kind, string purpose)
    {
        return new RekallAgeTemplateDrawCommand(id, kind, purpose);
    }

    private static IReadOnlyList<RekallAgeEntityDocument> Base2D()
    {
        return
        [
            Entity("MainCamera", ["camera"], Component("Rekall.Camera2D", Props(("active", true), ("clearColor", "#102030"))))
        ];
    }

    private static IReadOnlyList<RekallAgeEntityDocument> Base3D()
    {
        return
        [
            Entity("MainCamera", ["camera"], Component("Rekall.Camera3D", Props(("active", true), ("fieldOfView", 65)))),
            Entity("Sun", ["light"], Component("Rekall.DirectionalLight", Props(("intensity", 1.0))))
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

    private static RekallAgeComponentDocument TemplateComponent(string templateId, string name, JsonObject properties)
    {
        return Component($"Game.Templates.{ToTemplateNamespace(templateId)}.{name}", properties);
    }

    private static string ToTemplateNamespace(string templateId)
    {
        var parts = templateId.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Concat(parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static JsonObject Props(params (string Key, object? Value)[] values)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in values)
        {
            obj[key] = value is JsonNode node ? node.DeepClone() : JsonValue.Create(value);
        }

        return obj;
    }

    private static JsonObject Action(string name, params (string Key, object? Value)[] values)
    {
        var obj = Props(("name", name));
        foreach (var (key, value) in values)
        {
            obj[key] = value is JsonNode node ? node.DeepClone() : JsonValue.Create(value);
        }

        return obj;
    }
}
