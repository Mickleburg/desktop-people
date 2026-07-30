using DesktopPeople.Core;

var tests = new (string Name, Action Execute)[]
{
    ("state machine follows the runtime lifecycle", StateMachineLifecycle),
    ("state machine can be disabled safely", StateMachineDisable),
    ("falling body lands on the desktop", PhysicsLanding),
    ("held body follows the pointer", PhysicsHold),
    ("settings survive a round trip", SettingsRoundTrip),
    ("avatar manifest survives a round trip", AvatarRoundTrip),
    ("avatar manifest rejects escaping paths", AvatarRejectsTraversal),
};

int failures = 0;
foreach ((string name, Action execute) in tests)
{
    try
    {
        execute();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL  {name}");
        Console.Error.WriteLine(exception);
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void StateMachineLifecycle()
{
    var machine = new CharacterStateMachine();
    Equal(CharacterState.Spawn, machine.Current);
    True(machine.Send(CharacterSignal.Tick));
    Equal(CharacterState.Fall, machine.Current);
    True(machine.Send(CharacterSignal.Landed));
    Equal(CharacterState.Idle, machine.Current);
    True(machine.Send(CharacterSignal.WalkRequested));
    Equal(CharacterState.Walk, machine.Current);
    True(machine.Send(CharacterSignal.Grabbed));
    Equal(CharacterState.HeldByMouse, machine.Current);
    True(machine.Send(CharacterSignal.Released));
    Equal(CharacterState.Fall, machine.Current);
}

static void StateMachineDisable()
{
    var machine = new CharacterStateMachine();
    machine.Send(CharacterSignal.Disable);
    Equal(CharacterState.Disabled, machine.Current);
    True(!machine.Send(CharacterSignal.WalkRequested));
    machine.Send(CharacterSignal.Enable);
    Equal(CharacterState.Fall, machine.Current);
}

static void PhysicsLanding()
{
    var physics = new CharacterPhysics(new Vec2(50, 0), new Size2(20, 40));
    bool landed = false;
    for (int index = 0; index < 300 && !landed; index++)
    {
        landed = physics.Step(1d / 60, CharacterState.Fall, 200, 0, 300).Landed;
    }

    True(landed);
    Near(160, physics.Position.Y, 0.001);
    Near(0, physics.Velocity.Y, 0.001);
}

static void PhysicsHold()
{
    var physics = new CharacterPhysics(Vec2.Zero, new Size2(20, 40));
    physics.HoldAt(new Vec2(70, 80));
    Equal(new Vec2(60, 60), physics.Position);
    Equal(Vec2.Zero, physics.Velocity);
}

static void SettingsRoundTrip()
{
    string directory = Path.Combine(Path.GetTempPath(), "DesktopPeople.Tests", Guid.NewGuid().ToString("N"));
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
        };
        store.Save(expected);
        Equal(expected, store.Load());
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}

static void AvatarRoundTrip()
{
    var manifest = ValidManifest();
    string json = AvatarManifestSerializer.Serialize(manifest);
    Equal(manifest, AvatarManifestSerializer.Deserialize(json));
}

static void AvatarRejectsTraversal()
{
    AvatarManifest manifest = ValidManifest() with { Rig = "../outside.json" };
    Throws<InvalidDataException>(() => AvatarManifestSerializer.Serialize(manifest));
}

static AvatarManifest ValidManifest() => new()
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

static void True(bool value)
{
    if (!value)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

static void Equal<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
    }
}

static void Near(double expected, double actual, double tolerance)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"Expected {expected} ± {tolerance}, received {actual}.");
    }
}

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

