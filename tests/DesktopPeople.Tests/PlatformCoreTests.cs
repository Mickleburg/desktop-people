using System.Collections.Immutable;
using DesktopPeople.Core;
using DesktopPeople.Core.Platforms;

namespace DesktopPeople.Tests;

internal static class PlatformCoreTests
{
    private static readonly RectD VirtualScreen = new(-1920, -400, 4480, 1840);

    public static TestCase[] All =>
    [
        new("visible normal window becomes a platform candidate", VisibleWindowIncluded),
        new("invisible window is excluded", InvisibleWindowExcluded),
        new("minimized window is excluded", MinimizedWindowExcluded),
        new("own process window is excluded", OwnWindowExcluded),
        new("explicit overlay handle is excluded", OverlayWindowExcluded),
        new("too small window is excluded", SmallWindowExcluded),
        new("service class name is excluded", ServiceWindowExcluded),
        new("screen coordinates map into overlay coordinates", CoordinateMapping),
        new("negative monitor coordinates are preserved", NegativeCoordinates),
        new("DPI conversion does not introduce an origin offset", DpiScaling),
        new("falling character lands on a crossed surface", CollisionLanding),
        new("upward character does not land", CollisionIgnoresUpwardMotion),
        new("swept collision prevents tunnelling", SweptCollision),
        new("nearest crossed platform wins", NearestPlatformWins),
        new("landing without monitor headroom is rejected", CollisionRejectsSegmentWithoutHeadroom),
        new("attachment stores the relative foot position", AttachmentStoresRelativePosition),
        new("support without monitor headroom is rejected", AttachmentRejectsSegmentWithoutHeadroom),
        new("moving platform moves the attached character", AttachmentFollowsMovement),
        new("removed platform changes state to fall", RemovedPlatformCausesFall),
        new("shrinking platform causes fall when support disappears", ResizeCanRemoveSupport),
        new("shrinking platform keeps attachment when support remains", ResizeCanKeepSupport),
        new("resizing from the left edge does not drag the character", ResizeFromLeftEdgeKeepsAbsolutePosition),
        new("left edge passing under the character causes fall", ResizeFromLeftEdgePastCharacterCausesFall),
        new("occlusion policy exposes only visible top segments", OcclusionSplitsSegments),
    ];

    private static void VisibleWindowIncluded() =>
        AssertEx.True(Filter().Evaluate(Candidate(), VirtualScreen).Accepted);

    private static void InvisibleWindowExcluded() =>
        AssertReason(Candidate() with { IsVisible = false }, WindowExclusionReason.Invisible);

    private static void MinimizedWindowExcluded() =>
        AssertReason(Candidate() with { IsMinimized = true }, WindowExclusionReason.Minimized);

    private static void OwnWindowExcluded() =>
        AssertReason(Candidate() with { ProcessId = 42 }, WindowExclusionReason.OwnProcess);

    private static void OverlayWindowExcluded()
    {
        WindowFilterResult result = Filter().Evaluate(Candidate(), VirtualScreen, new HashSet<long> { 10 });
        AssertEx.Equal(WindowExclusionReason.ExplicitlyExcluded, result.Reason);
    }

    private static void SmallWindowExcluded() =>
        AssertReason(
            Candidate() with { ScreenBounds = new RectD(10, 10, 80, 20) },
            WindowExclusionReason.TooSmall);

    private static void ServiceWindowExcluded() =>
        AssertReason(Candidate() with { ClassName = "WorkerW" }, WindowExclusionReason.ServiceClass);

    private static void CoordinateMapping()
    {
        var mapper = new CoordinateMapper();
        RectD mapped = mapper.ScreenToOverlay(
            new RectD(100, 200, 640, 480),
            new RectD(-300, -100, 2_000, 1_200));
        AssertEx.Equal(new RectD(400, 300, 640, 480), mapped);
    }

    private static void NegativeCoordinates()
    {
        var mapper = new CoordinateMapper();
        RectD mapped = mapper.ScreenToOverlay(
            new RectD(-1_800, -200, 800, 600),
            new RectD(-1_920, -400, 4_480, 1_840));
        AssertEx.Equal(new RectD(120, 200, 800, 600), mapped);
    }

    private static void DpiScaling()
    {
        var mapper = new CoordinateMapper();
        RectD logical = new(-100, 40, 400, 200);
        AssertEx.Equal(logical, mapper.LogicalToPhysical(logical, 96));
        AssertEx.Equal(new RectD(-125, 50, 500, 250), mapper.LogicalToPhysical(logical, 120));
    }

    private static void CollisionLanding()
    {
        var resolver = new PlatformCollisionResolver();
        DesktopPlatform platform = Platform("window:1", 0, 150, 300);
        PlatformCollision? collision = resolver.ResolveDownward(
            new RectD(100, 50, 40, 50),
            new RectD(100, 120, 40, 50),
            500,
            [platform]);
        AssertEx.Equal("window:1", collision!.Value.Platform.Id);
    }

    private static void CollisionIgnoresUpwardMotion()
    {
        var resolver = new PlatformCollisionResolver();
        PlatformCollision? collision = resolver.ResolveDownward(
            new RectD(100, 120, 40, 50),
            new RectD(100, 50, 40, 50),
            -500,
            [Platform("window:1", 0, 100, 300)]);
        AssertEx.True(collision is null);
    }

    private static void SweptCollision()
    {
        var resolver = new PlatformCollisionResolver();
        PlatformCollision? collision = resolver.ResolveDownward(
            new RectD(100, 0, 40, 40),
            new RectD(100, 220, 40, 40),
            1_500,
            [Platform("thin", 0, 120, 300)]);
        AssertEx.Equal("thin", collision!.Value.Platform.Id);
    }

    private static void NearestPlatformWins()
    {
        var resolver = new PlatformCollisionResolver();
        PlatformCollision? collision = resolver.ResolveDownward(
            new RectD(100, 0, 40, 40),
            new RectD(100, 220, 40, 40),
            1_500,
            [Platform("lower", 0, 190, 300), Platform("upper", 0, 100, 300)]);
        AssertEx.Equal("upper", collision!.Value.Platform.Id);
    }

    private static void CollisionRejectsSegmentWithoutHeadroom()
    {
        var resolver = new PlatformCollisionResolver();
        DesktopPlatform platform = Platform("window:1", 0, 150, 300) with { MonitorTop = 150 };
        PlatformCollision? collision = resolver.ResolveDownward(
            new RectD(100, 50, 40, 50),
            new RectD(100, 120, 40, 50),
            500,
            [platform]);
        AssertEx.True(collision is null);
    }

    private static void AttachmentRejectsSegmentWithoutHeadroom()
    {
        var attachment = new CharacterPlatformAttachment();
        DesktopPlatform platform = Platform("window:1", 100, 200, 200) with { MonitorTop = 200 };
        PlatformSegment? segment = attachment.FindSupportingSegment(platform, new RectD(140, 110, 20, 40));
        AssertEx.True(segment is null);
    }

    private static void AttachmentStoresRelativePosition()
    {
        var attachment = new CharacterPlatformAttachment();
        attachment.Attach(Platform("window:1", 100, 200, 200), new RectD(140, 160, 20, 40));
        AssertEx.Near(50, attachment.RelativeFootCenterX);
        AssertEx.Near(0, attachment.VerticalOffset);
    }

    private static void AttachmentFollowsMovement()
    {
        var attachment = new CharacterPlatformAttachment();
        RectD character = new(140, 160, 20, 40);
        attachment.Attach(Platform("window:1", 100, 200, 200), character);
        bool followed = attachment.TryFollow(
            Platform("window:1", 300, 250, 200),
            character,
            out Vec2 target);
        AssertEx.True(followed);
        AssertEx.Equal(new Vec2(340, 210), target);
    }

    private static void RemovedPlatformCausesFall()
    {
        var attachment = new CharacterPlatformAttachment();
        var controller = new CharacterPlatformController(attachment);
        var physics = new CharacterPhysics(new Vec2(140, 160), new Size2(20, 40));
        var state = IdleStateMachine();
        attachment.Attach(Platform("window:1", 100, 200, 200), physics.Bounds);
        bool followed = controller.TryFollow(
            PlatformSnapshot.Empty,
            physics,
            state,
            out _,
            out string? lostPlatform);
        AssertEx.False(followed);
        AssertEx.Equal("window:1", lostPlatform!);
        AssertEx.Equal(CharacterState.Fall, state.Current);
    }

    private static void ResizeCanRemoveSupport()
    {
        var attachment = new CharacterPlatformAttachment();
        RectD character = new(170, 160, 20, 40);
        attachment.Attach(Platform("window:1", 100, 200, 200), character);
        AssertEx.False(attachment.TryFollow(
            Platform("window:1", 100, 200, 60),
            character,
            out _));
    }

    private static void ResizeCanKeepSupport()
    {
        var attachment = new CharacterPlatformAttachment();
        RectD character = new(170, 160, 20, 40);
        attachment.Attach(Platform("window:1", 100, 200, 200), character);
        AssertEx.True(attachment.TryFollow(
            Platform("window:1", 100, 200, 100),
            character,
            out Vec2 target));
        AssertEx.Equal(new Vec2(170, 160), target);
    }

    private static void ResizeFromLeftEdgeKeepsAbsolutePosition()
    {
        var attachment = new CharacterPlatformAttachment();
        RectD character = new(140, 160, 20, 40);
        attachment.Attach(Platform("window:1", 100, 200, 200), character);
        bool followed = attachment.TryFollow(
            Platform("window:1", 130, 200, 170),
            character,
            out Vec2 target);
        AssertEx.True(followed);
        AssertEx.Equal(new Vec2(140, 160), target);
    }

    private static void ResizeFromLeftEdgePastCharacterCausesFall()
    {
        var attachment = new CharacterPlatformAttachment();
        RectD character = new(140, 160, 20, 40);
        attachment.Attach(Platform("window:1", 100, 200, 200), character);
        bool followed = attachment.TryFollow(
            Platform("window:1", 160, 200, 140),
            character,
            out _);
        AssertEx.False(followed);
    }

    private static void OcclusionSplitsSegments()
    {
        var policy = new TopEdgeVisibilityPolicy(10);
        DesktopPlatform back = Platform("back", 0, 100, 200, 1);
        DesktopPlatform front = Platform("front", 50, 52, 50, 0) with
        {
            Bounds = new RectD(50, 50, 50, 100),
        };
        ImmutableArray<DesktopPlatform> result = policy.Apply([front, back]);
        DesktopPlatform visibleBack = result.Single(platform => platform.Id == "back");
        AssertEx.Equal(2, visibleBack.Segments.Length);
        AssertEx.Equal(new PlatformSegment(0, 50, 100), visibleBack.Segments[0]);
        AssertEx.Equal(new PlatformSegment(100, 200, 100), visibleBack.Segments[1]);
    }

    private static CharacterStateMachine IdleStateMachine()
    {
        var state = new CharacterStateMachine();
        state.Send(CharacterSignal.Tick);
        state.Send(CharacterSignal.Landed);
        return state;
    }

    private static WindowFilter Filter() => new(42);

    private static void AssertReason(WindowCandidate candidate, WindowExclusionReason reason)
    {
        WindowFilterResult result = Filter().Evaluate(candidate, VirtualScreen);
        AssertEx.False(result.Accepted);
        AssertEx.Equal(reason, result.Reason);
    }

    private static WindowCandidate Candidate() => new()
    {
        Handle = 10,
        IsValid = true,
        IsVisible = true,
        ScreenBounds = new RectD(100, 100, 800, 600),
        ProcessId = 99,
        ClassName = "Notepad",
    };

    internal static DesktopPlatform Platform(
        string id,
        double left,
        double surface,
        double width,
        int zOrder = 0) => new()
        {
            Id = id,
            Kind = PlatformKind.Window,
            ExternalHandle = id.GetHashCode(StringComparison.Ordinal),
            Bounds = new RectD(left, surface, width, 100),
            Segments = [new PlatformSegment(left, left + width, surface)],
            ZOrder = zOrder,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
}
