using DesktopPeople.Core;

namespace DesktopPeople.Tests;

internal static class LegacyTests
{
    public static TestCase[] All =>
    [
        new("state machine follows the runtime lifecycle", StateMachineLifecycle),
        new("state machine can be disabled safely", StateMachineDisable),
        new("falling body lands on the desktop", PhysicsLanding),
        new("held body follows the pointer", PhysicsHold),
        new("settings survive a round trip", SettingsRoundTrip),
        new("avatar manifest survives a round trip", AvatarRoundTrip),
        new("avatar manifest rejects escaping paths", AvatarRejectsTraversal),
    ];

    private static void StateMachineLifecycle()
    {
        var machine = new CharacterStateMachine();
        AssertEx.Equal(CharacterState.Spawn, machine.Current);
        AssertEx.True(machine.Send(CharacterSignal.Tick));
        AssertEx.Equal(CharacterState.Fall, machine.Current);
        AssertEx.True(machine.Send(CharacterSignal.Landed));
        AssertEx.Equal(CharacterState.Idle, machine.Current);
        AssertEx.True(machine.Send(CharacterSignal.WalkRequested));
        AssertEx.Equal(CharacterState.Walk, machine.Current);
        AssertEx.True(machine.Send(CharacterSignal.Grabbed));
        AssertEx.Equal(CharacterState.HeldByMouse, machine.Current);
        AssertEx.True(machine.Send(CharacterSignal.Released));
        AssertEx.Equal(CharacterState.Fall, machine.Current);
    }

    private static void StateMachineDisable()
    {
        var machine = new CharacterStateMachine();
        machine.Send(CharacterSignal.Disable);
        AssertEx.Equal(CharacterState.Disabled, machine.Current);
        AssertEx.False(machine.Send(CharacterSignal.WalkRequested));
        machine.Send(CharacterSignal.Enable);
        AssertEx.Equal(CharacterState.Fall, machine.Current);
    }

    private static void PhysicsLanding()
    {
        var physics = new CharacterPhysics(new Vec2(50, 0), new Size2(20, 40));
        bool landed = false;
        for (int index = 0; index < 300 && !landed; index++)
        {
            landed = physics.Step(1d / 60, CharacterState.Fall, 200, 0, 300).Landed;
        }

        AssertEx.True(landed);
        AssertEx.Near(160, physics.Position.Y);
        AssertEx.Near(0, physics.Velocity.Y);
    }

    private static void PhysicsHold()
    {
        var physics = new CharacterPhysics(Vec2.Zero, new Size2(20, 40));
        physics.HoldAt(new Vec2(70, 80));
        AssertEx.Equal(new Vec2(60, 60), physics.Position);
        AssertEx.Equal(Vec2.Zero, physics.Velocity);
    }

    private static void SettingsRoundTrip()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DesktopPeople.Tests",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new SettingsStore(path);
            var expected = new AppSettings
            {
                TargetFps = 30,
                IsPaused = true,
                CharactersVisible = false,
                BehaviorIntensity = "calm",
                ShowPlatformDebug = true,
            };
            store.Save(expected);
            AssertEx.Equal(expected, store.Load());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static void AvatarRoundTrip()
    {
        AvatarManifest manifest = ValidManifest();
        string json = AvatarManifestSerializer.Serialize(manifest);
        AssertEx.Equal(manifest, AvatarManifestSerializer.Deserialize(json));
    }

    private static void AvatarRejectsTraversal()
    {
        AvatarManifest manifest = ValidManifest() with { Rig = "../outside.json" };
        AssertEx.Throws<InvalidDataException>(() => AvatarManifestSerializer.Serialize(manifest));
    }

    private static AvatarManifest ValidManifest() => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = "Test person",
        CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        SourceType = "full_body",
        BodyCompletionUsed = false,
        HeightPx = 260,
        Rig = "rig/skeleton.json",
        GenerationVersion = "prototype",
    };
}
