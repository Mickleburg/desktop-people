using System.Collections.Immutable;
using DesktopPeople.Core;
using DesktopPeople.Core.Platforms;

namespace DesktopPeople.Tests;

/// <summary>
/// Behaviour that was untestable while it lived inside the WinForms <c>OverlayForm</c>.
/// Every bug covered here reached a real user first — the character walking off under the
/// screen, corrupted state crashing the renderer on every frame and leaving an unclosable
/// window — precisely because there was no way to assert on any of it without a live window.
/// </summary>
internal static class CharacterSimulationTests
{
    private const double Frame = 1.0 / 60;

    public static TestCase[] All =>
    [
        new("simulation lands a dropped character on the desktop floor", LandsOnDesktopFloor),
        new("simulation does not advance while the character is hidden", FrozenWhileHidden),
        new("simulation recovers from a non-finite position", RecoversFromNaNPosition),
        new("simulation recovers from a corrupted character size", RecoversFromBadSize),
        new("simulation recovers after the renderer reports a failure", RecoversAfterRenderFailure),
        new("grab only takes hold when the pointer is on the character", GrabRequiresHit),
        new("a flick throws the character, a tap only nudges it", ThrowCarriesDragVelocity),
    ];

    private static void LandsOnDesktopFloor()
    {
        CharacterSimulation simulation = Create();
        simulation.Start(Screen, Screen);

        // Falls from the spawn point; the bare desktop floor is the one absolute boundary.
        for (int i = 0; i < 600; i++)
        {
            simulation.Update(Frame, new Vec2(-10_000, -10_000), visible: true);
        }

        AssertEx.True(Grounded(simulation.State));
        AssertEx.True(simulation.CurrentFrame().Body.Bottom <= StubScreens.FloorY + 1);
    }

    private static void FrozenWhileHidden()
    {
        CharacterSimulation simulation = Create();
        simulation.Start(Screen, Screen);
        RectD before = simulation.CurrentFrame().Body;

        for (int i = 0; i < 200; i++)
        {
            simulation.Update(Frame, Vec2.Zero, visible: false);
        }

        // Without this guard the character walks around on an invisible floor the whole time
        // the launcher is open and pops up mid-behaviour the moment it is finally released.
        AssertEx.Equal(before, simulation.CurrentFrame().Body);
    }

    private static void RecoversFromNaNPosition()
    {
        CharacterSimulation simulation = Create();
        simulation.Start(Screen, Screen);
        simulation.TryGrab(simulation.CurrentFrame().Body.Centre(), 0);
        simulation.Drag(new Vec2(double.NaN, double.NaN), 1);

        simulation.Update(Frame, Vec2.Zero, visible: true);

        RectD body = simulation.CurrentFrame().Body;
        AssertEx.True(double.IsFinite(body.X) && double.IsFinite(body.Y));
        AssertEx.True(Screen.Inflate(5_000, 5_000).Contains(new Vec2(body.X, body.Y)));
    }

    private static void RecoversFromBadSize()
    {
        CharacterSimulation simulation = Create();
        simulation.Start(Screen, Screen);

        // CharacterScale clamps, so corrupt the size the way the real defect did — through a
        // scale that is not a number at all.
        simulation.CharacterScale = double.NaN;
        simulation.Update(Frame, Vec2.Zero, visible: true);

        RectD body = simulation.CurrentFrame().Body;
        AssertEx.True(double.IsFinite(body.Width) && double.IsFinite(body.Height));
        AssertEx.True(body.Width > 0 && body.Height > 0);
    }

    private static void RecoversAfterRenderFailure()
    {
        CharacterSimulation simulation = Create();
        simulation.Start(Screen, Screen);

        simulation.NotifyRenderFailed("boom");

        RectD body = simulation.CurrentFrame().Body;
        AssertEx.True(double.IsFinite(body.X) && double.IsFinite(body.Y));
        AssertEx.True(body.Width > 0 && body.Height > 0);
    }

    private static void GrabRequiresHit()
    {
        CharacterSimulation simulation = Create();
        simulation.Start(Screen, Screen);

        AssertEx.False(simulation.TryGrab(new Vec2(5_000, 5_000), 0));
        AssertEx.False(simulation.IsHeld);

        AssertEx.True(simulation.TryGrab(simulation.CurrentFrame().Body.Centre(), 0));
        AssertEx.True(simulation.IsHeld);
    }

    private static void ThrowCarriesDragVelocity()
    {
        CharacterSimulation thrown = Create();
        thrown.Start(Screen, Screen);
        Vec2 start = thrown.CurrentFrame().Body.Centre();
        thrown.TryGrab(start, 0);
        thrown.Drag(start + new Vec2(400, 0), 0.05);
        thrown.ReleaseGrab(start + new Vec2(400, 0));

        CharacterSimulation tapped = Create();
        tapped.Start(Screen, Screen);
        Vec2 tapPoint = tapped.CurrentFrame().Body.Centre();
        tapped.TryGrab(tapPoint, 0);
        tapped.ReleaseGrab(tapPoint);

        double thrownTravel = Travel(thrown, start);
        double tappedTravel = Travel(tapped, tapPoint);
        AssertEx.True(thrownTravel > tappedTravel);
    }

    private static double Travel(CharacterSimulation simulation, Vec2 from)
    {
        for (int i = 0; i < 20; i++)
        {
            simulation.Update(Frame, new Vec2(-10_000, -10_000), visible: true);
        }

        return Math.Abs(simulation.CurrentFrame().Body.Centre().X - from.X);
    }

    private static bool Grounded(CharacterState state) =>
        state is CharacterState.Idle or CharacterState.Walk or CharacterState.Run or CharacterState.Sit;

    private static RectD Screen => new(0, 0, 1920, 1080);

    private static CharacterSimulation Create() =>
        new(NullOverlayLogger.Instance, new StubPlatforms(), new StubScreens());

    private static Vec2 Centre(this RectD rect) =>
        new(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));

    /// <summary>A desktop with no windows on it: the character only ever has the bare floor
    /// and the screen edges to work with, which keeps these tests about the simulation rather
    /// than about window enumeration.</summary>
    private sealed class StubPlatforms : IWindowPlatformProvider
    {
        public PlatformSnapshot Snapshot { get; } = new()
        {
            Platforms = ImmutableArray<DesktopPlatform>.Empty,
            CapturedAt = DateTimeOffset.UnixEpoch,
        };

        public void Start(RectD overlayScreenBounds, RectD virtualScreenBounds)
        {
        }

        public void Pump(DateTimeOffset now, RectD overlayScreenBounds, RectD virtualScreenBounds)
        {
        }

        public void SetExplicitlyExcludedHandles(IEnumerable<long> handles)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class StubScreens : IScreenGeometry
    {
        public const double FloorY = 1040;

        public RectD VirtualBounds => new(0, 0, 1920, 1080);

        public int MonitorCount => 1;

        public RectD WorkAreaAt(Vec2 overlayPoint) => new(0, 0, 1920, FloorY);

        public double MonitorTopAt(Vec2 overlayPoint) => 0;

        public RectD PrimaryWorkArea => new(0, 0, 1920, FloorY);
    }
}
