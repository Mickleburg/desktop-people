using DesktopPeople.Core;

namespace DesktopPeople.Tests;

internal static class LegacyTests
{
    public static TestCase[] All =>
    [
        new("state machine follows the runtime lifecycle", StateMachineLifecycle),
        new("state machine can be disabled safely", StateMachineDisable),
        new("state machine supports run and sit", StateMachineRunAndSit),
        new("support lost falls from run or sit", StateMachineSupportLostFromRunOrSit),
        new("falling character can grab a wall mid-air", StateMachineFallGrabsWall),
        new("walking or running character can hide behind a wall", StateMachineHideFromWalkAndRun),
        new("hiding character falls when its wall moves away and the grab roll fails", StateMachineHideEndsInFallOnSupportLost),
        new("falling body lands on the desktop", PhysicsLanding),
        new("held body follows the pointer", PhysicsHold),
        new("rescaling keeps the character's feet planted", PhysicsRescaleKeepsFeetPlanted),
        new("running body covers more ground than walking", PhysicsRunIsFasterThanWalk),
        new("behavior tuning picks sit, run, or walk by roll", BehaviorTuningPicksByRoll),
        new("behavior tuning maps known intensities", BehaviorTuningMapsIntensities),
        new("behavior tuning picks jump before sit/run/walk", BehaviorTuningPicksJumpFirst),
        new("cursor energy nudges tuning toward the next profile", BehaviorTuningReactsToEnergy),
        new("attention tracks the cursor near interaction or proximity", AttentionTracksNearCursor),
        new("attention looks away when idle and far", AttentionLooksAwayWhenIdle),
        new("harassment accrues near the cursor and decays away from it", HarassmentAccruesAndDecays),
        new("harassment interaction bump can trigger flee", HarassmentInteractionTriggersFlee),
        new("settings survive a round trip", SettingsRoundTrip),
        new("character scale is clamped to a sane range", AppSettingsClampsScale),
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

    private static void StateMachineRunAndSit()
    {
        var machine = new CharacterStateMachine();
        machine.Send(CharacterSignal.Tick);
        machine.Send(CharacterSignal.Landed);
        AssertEx.True(machine.Send(CharacterSignal.RunRequested));
        AssertEx.Equal(CharacterState.Run, machine.Current);
        AssertEx.True(machine.Send(CharacterSignal.SitRequested));
        AssertEx.Equal(CharacterState.Sit, machine.Current);
        AssertEx.True(machine.Send(CharacterSignal.StandRequested));
        AssertEx.Equal(CharacterState.Idle, machine.Current);
    }

    private static void StateMachineSupportLostFromRunOrSit()
    {
        var machine = new CharacterStateMachine();
        machine.Send(CharacterSignal.Tick);
        machine.Send(CharacterSignal.Landed);
        machine.Send(CharacterSignal.SitRequested);
        AssertEx.True(machine.Send(CharacterSignal.SupportLost));
        AssertEx.Equal(CharacterState.Fall, machine.Current);
    }

    private static void StateMachineFallGrabsWall()
    {
        var machine = new CharacterStateMachine();
        machine.Send(CharacterSignal.Tick);
        AssertEx.Equal(CharacterState.Fall, machine.Current);
        AssertEx.True(machine.Send(CharacterSignal.ClimbRequested));
        AssertEx.Equal(CharacterState.Climb, machine.Current);
    }

    private static void StateMachineHideFromWalkAndRun()
    {
        var machine = new CharacterStateMachine();
        machine.Send(CharacterSignal.Tick);
        machine.Send(CharacterSignal.Landed);
        machine.Send(CharacterSignal.WalkRequested);
        AssertEx.True(machine.Send(CharacterSignal.HideRequested));
        AssertEx.Equal(CharacterState.Hide, machine.Current);
        AssertEx.True(machine.Send(CharacterSignal.ClimbRequested));
        AssertEx.Equal(CharacterState.Climb, machine.Current);
    }

    private static void StateMachineHideEndsInFallOnSupportLost()
    {
        var machine = new CharacterStateMachine();
        machine.Send(CharacterSignal.Tick);
        machine.Send(CharacterSignal.Landed);
        machine.Send(CharacterSignal.RunRequested);
        machine.Send(CharacterSignal.HideRequested);
        AssertEx.Equal(CharacterState.Hide, machine.Current);
        AssertEx.True(machine.Send(CharacterSignal.SupportLost));
        AssertEx.Equal(CharacterState.Fall, machine.Current);
    }

    private static void PhysicsRescaleKeepsFeetPlanted()
    {
        var physics = new CharacterPhysics(new Vec2(100, 50), new Size2(40, 80));
        physics.Rescale(new Size2(20, 40));
        AssertEx.Equal(new Size2(20, 40), physics.Size);
        AssertEx.Near(110, physics.Position.X);
        AssertEx.Near(90, physics.Position.Y);
    }

    private static void PhysicsRunIsFasterThanWalk()
    {
        var walker = new CharacterPhysics(Vec2.Zero, new Size2(20, 40));
        var runner = new CharacterPhysics(Vec2.Zero, new Size2(20, 40));
        walker.Integrate(1, CharacterState.Walk, -10_000, 10_000);
        runner.Integrate(1, CharacterState.Run, -10_000, 10_000);
        AssertEx.True(runner.Position.X > walker.Position.X);
    }

    private static void BehaviorTuningPicksByRoll()
    {
        var tuning = new CharacterBehaviorTuning(
            IdleDelay: 1,
            WalkDuration: 1,
            RunDuration: 1,
            SitDuration: 1,
            SitChance: 0.3,
            RunChance: 0.2);
        AssertEx.Equal(CharacterSignal.SitRequested, tuning.PickAutonomousTransition(0.1));
        AssertEx.Equal(CharacterSignal.RunRequested, tuning.PickAutonomousTransition(0.4));
        AssertEx.Equal(CharacterSignal.WalkRequested, tuning.PickAutonomousTransition(0.9));
    }

    private static void BehaviorTuningMapsIntensities()
    {
        CharacterBehaviorTuning calm = CharacterBehaviorTuning.ForIntensity("calm");
        CharacterBehaviorTuning active = CharacterBehaviorTuning.ForIntensity("active");
        CharacterBehaviorTuning fallback = CharacterBehaviorTuning.ForIntensity("unknown");
        AssertEx.Equal(CharacterBehaviorTuning.ForIntensity("normal"), fallback);
        AssertEx.True(active.RunChance > calm.RunChance);
        AssertEx.True(calm.SitChance > active.SitChance);
        AssertEx.True(calm.HideChance > active.HideChance);
        AssertEx.True(active.GrabChance > calm.GrabChance);
    }

    private static void BehaviorTuningPicksJumpFirst()
    {
        var tuning = new CharacterBehaviorTuning(
            IdleDelay: 1,
            WalkDuration: 1,
            RunDuration: 1,
            SitDuration: 1,
            SitChance: 0.3,
            RunChance: 0.2,
            JumpChance: 0.1);
        AssertEx.Equal(CharacterSignal.JumpRequested, tuning.PickAutonomousTransition(0.05));
        AssertEx.Equal(CharacterSignal.SitRequested, tuning.PickAutonomousTransition(0.2));
    }

    private static void BehaviorTuningReactsToEnergy()
    {
        CharacterBehaviorTuning baseline = CharacterBehaviorTuning.ForIntensity("normal");
        CharacterBehaviorTuning energetic = CharacterBehaviorTuning.ForEnergy("normal", 1.0);
        CharacterBehaviorTuning still = CharacterBehaviorTuning.ForEnergy("normal", 0.0);
        AssertEx.True(energetic.RunChance > baseline.RunChance);
        AssertEx.True(still.SitChance > baseline.SitChance);
        AssertEx.Equal(baseline, CharacterBehaviorTuning.ForEnergy("normal", 0.5));
    }

    private static void AttentionTracksNearCursor()
    {
        var attention = new CharacterAttention();
        AssertEx.True(attention.ShouldTrackCursor(now: 10, secondsSinceInteraction: 1, distanceToCursor: 500));
        AssertEx.True(attention.ShouldTrackCursor(now: 10, secondsSinceInteraction: 999, distanceToCursor: 10));
    }

    private static void AttentionLooksAwayWhenIdle()
    {
        var attention = new CharacterAttention(proximityRadius: 90, interactionWindowSeconds: 4);
        AssertEx.False(attention.ShouldTrackCursor(now: 0, secondsSinceInteraction: 999, distanceToCursor: 500));
    }

    private static void HarassmentAccruesAndDecays()
    {
        var tracker = new CharacterHarassmentTracker(proximityRadius: 50, fleeThreshold: 2, decayPerSecond: 1);
        tracker.Update(deltaSeconds: 2.5, distanceToCursor: 10);
        AssertEx.True(tracker.IsFleeing);
        tracker.Update(deltaSeconds: 5, distanceToCursor: 500);
        AssertEx.False(tracker.IsFleeing);
    }

    private static void HarassmentInteractionTriggersFlee()
    {
        var tracker = new CharacterHarassmentTracker(fleeThreshold: 1, clickBump: 1.5);
        AssertEx.False(tracker.IsFleeing);
        tracker.RegisterInteraction();
        AssertEx.True(tracker.IsFleeing);
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
                CharacterScale = 1.3,
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

    private static void AppSettingsClampsScale()
    {
        AppSettings tooLarge = new AppSettings { CharacterScale = 5.0 }.Normalize();
        AssertEx.Near(1.6, tooLarge.CharacterScale);
        AppSettings tooSmall = new AppSettings { CharacterScale = 0.1 }.Normalize();
        AssertEx.Near(0.7, tooSmall.CharacterScale);
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
