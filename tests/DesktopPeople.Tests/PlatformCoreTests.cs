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
        new("a fall starting below a surface can never land on it", FallStartingBelowMissesTheSurface),
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
        new("jumping into a ceiling bonks the character", CeilingBonkOnUpwardMotion),
        new("downward motion never bonks a ceiling", CeilingIgnoresDownwardMotion),
        new("nearest ceiling from below wins", NearestCeilingWins),
        new("wall climb advances downward and stops at the wall bottom", WallClimbReachesBottom),
        new("wall climb reaching the top reports the standing surface", WallClimbReachesTop),
        new("wall climb clings to the requested side", WallClimbPicksSide),
        new("wall climb eases onto the ledge near the top instead of snapping", WallClimbBlendsOntoLedgeNearTop),
        new("wall climb reports losing contact with the wall across the ledge blend", WallClimbReportsWallContact),
        new("wall edge covered by a window in front is not usable", WallEdgeCoveredByFrontWindowRejected),
        new("wall edge is usable where the covering window does not reach", WallEdgeUsableOutsideCoveringWindow),
        new("stepping onto the ledge after reaching the top can be re-attached", ClimbToTopReattachesWithoutThrowing),
        new("wall side resolver picks the platform's own edge from walk direction", WallSideResolverOwnEdge),
        new("wall side resolver picks the encountered wall's near edge", WallSideResolverEncounteredWall),
        new("wall encounter detector finds a neighbor crossing the walking line", WallEncounterFindsNeighborAhead),
        new("wall encounter detector ignores a window that doesn't cross the line", WallEncounterIgnoresNonCrossingWindow),
        new("wall encounter detector ignores a wall covered by a window in front", WallEncounterIgnoresCoveredWall),
        new("wall grab detector finds an edge within reach while moving toward it", WallGrabFindsEdgeInReach),
        new("wall grab detector ignores an edge outside capture distance", WallGrabIgnoresOutOfReach),
        new("wall grab detector ignores a nearly stationary character", WallGrabIgnoresStationaryCharacter),
        new("integrate reports the pre-bounce approach direction on a right-edge hit", IntegrateReportsRightEdgeApproachDirection),
        new("integrate reports the pre-bounce approach direction on a left-edge hit", IntegrateReportsLeftEdgeApproachDirection),
        new("integrate reports no edge direction when no boundary is hit", IntegrateReportsNoEdgeDirectionMidFloor),
        new("wall climb retargets to a platform that moved mid-climb", WallClimbRetargetsToMovedPlatform),
        new("wall grab detector accepts a screen-edge platform", WallGrabAcceptsScreenEdgePlatform),
        new("wall grab detector ignores a desktop-kind platform", WallGrabIgnoresDesktopPlatform),
        new("wall climb clamps horizontally to the given screen bounds", WallClimbClampsHorizontallyToScreenBounds),
        new("wall climb clamps its bottom to the given screen floor", WallClimbClampsBottomToScreenFloor),
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

    /// <summary>The rule behind the "peeks out and falls" defect. Landing is a swept test: a
    /// surface only catches feet that CROSS it. Feet that are already past it are past it for
    /// good — so anything that quietly moves the body down before a fall begins (entering a
    /// hiding place used to, by 14% of the character's height) makes the surface it was standing
    /// on unable to ever catch it again.
    /// <para>
    /// Also pins down why it only showed up on a window stacked on another window: with nothing
    /// below but the desktop, the same miss just lands on the floor a few pixels lower and looks
    /// like nothing happened.
    /// </para>
    /// </summary>
    private static void FallStartingBelowMissesTheSurface()
    {
        var resolver = new PlatformCollisionResolver();
        DesktopPlatform surface = Platform("window:under", 0, 150, 300);

        // Starting exactly on the surface: the next downward step crosses it and lands.
        AssertEx.Equal(
            "window:under",
            resolver.ResolveDownward(
                new RectD(100, 100, 40, 50),
                new RectD(100, 108, 40, 50),
                400,
                [surface])!.Value.Platform.Id);

        // Starting a fraction below it: no crossing exists any more, at any speed.
        AssertEx.True(resolver.ResolveDownward(
            new RectD(100, 107, 40, 50),
            new RectD(100, 115, 40, 50),
            400,
            [surface]) is null);
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

    private static void CeilingBonkOnUpwardMotion()
    {
        var resolver = new PlatformCollisionResolver();
        DesktopPlatform platform = Platform("window:1", 0, 300, 200) with
        {
            CeilingSegments = [new PlatformSegment(0, 200, 150)],
        };
        PlatformCollision? collision = resolver.ResolveUpward(
            new RectD(100, 200, 40, 50),
            new RectD(100, 130, 40, 50),
            -1_500,
            [platform]);
        AssertEx.Equal("window:1", collision!.Value.Platform.Id);
    }

    private static void CeilingIgnoresDownwardMotion()
    {
        var resolver = new PlatformCollisionResolver();
        DesktopPlatform platform = Platform("window:1", 0, 300, 200) with
        {
            CeilingSegments = [new PlatformSegment(0, 200, 150)],
        };
        PlatformCollision? collision = resolver.ResolveUpward(
            new RectD(100, 130, 40, 50),
            new RectD(100, 200, 40, 50),
            1_500,
            [platform]);
        AssertEx.True(collision is null);
    }

    private static void NearestCeilingWins()
    {
        var resolver = new PlatformCollisionResolver();
        DesktopPlatform higher = Platform("higher", 0, 300, 200) with
        {
            CeilingSegments = [new PlatformSegment(0, 200, 50)],
        };
        DesktopPlatform lower = Platform("lower", 0, 300, 200) with
        {
            CeilingSegments = [new PlatformSegment(0, 200, 150)],
        };
        PlatformCollision? collision = resolver.ResolveUpward(
            new RectD(100, 220, 40, 40),
            new RectD(100, 0, 40, 40),
            -1_500,
            [higher, lower]);
        AssertEx.Equal("lower", collision!.Value.Platform.Id);
    }

    private static void WallClimbReachesBottom()
    {
        var climb = new CharacterWallClimb();
        DesktopPlatform platform = Platform("window:1", 100, 200, 200);
        climb.Start(platform, WallSide.Right, new Size2(20, 40));
        double startY = platform.Segments[0].SurfaceY - 40;
        ClimbStep step = climb.Advance(startY, 1_000);
        AssertEx.Equal(ClimbOutcome.ReachedBottom, step.Outcome);
        AssertEx.Near(platform.Bounds.Bottom - 40, step.Position.Y);
    }

    private static void WallClimbReachesTop()
    {
        var climb = new CharacterWallClimb();
        DesktopPlatform platform = Platform("window:1", 100, 200, 200);
        climb.Start(platform, WallSide.Right, new Size2(20, 40));
        double startY = platform.Bounds.Bottom - 40;
        ClimbStep step = climb.Advance(startY, -1_000);
        AssertEx.Equal(ClimbOutcome.ReachedTop, step.Outcome);
        AssertEx.Near(platform.Segments[0].SurfaceY - 40, step.Position.Y);
    }

    private static void WallClimbPicksSide()
    {
        // Advance well past the near-top ledge blend zone (see WallClimbBlendsOntoLedgeNearTop)
        // so this checks the steady-state clinging position, not the eased approach.
        var climb = new CharacterWallClimb();
        DesktopPlatform platform = Platform("window:1", 100, 200, 200);
        climb.Start(platform, WallSide.Left, new Size2(20, 40));
        ClimbStep leftStep = climb.Advance(platform.Segments[0].SurfaceY - 40, 40);
        AssertEx.Near(platform.Bounds.X - 20, leftStep.Position.X);

        climb.Start(platform, WallSide.Right, new Size2(20, 40));
        ClimbStep rightStep = climb.Advance(platform.Segments[0].SurfaceY - 40, 40);
        AssertEx.Near(platform.Bounds.Right, rightStep.Position.X);
    }

    private static void WallClimbBlendsOntoLedgeNearTop()
    {
        var climb = new CharacterWallClimb();
        DesktopPlatform platform = Platform("window:1", 100, 200, 200);
        climb.Start(platform, WallSide.Right, new Size2(20, 40));
        double topY = platform.Segments[0].SurfaceY - 40;

        ClimbStep farFromTop = climb.Advance(topY, 40);
        AssertEx.Near(platform.Bounds.Right, farFromTop.Position.X);

        ClimbStep nearTop = climb.Advance(topY, 6);
        AssertEx.True(nearTop.Position.X < platform.Bounds.Right);
        AssertEx.True(nearTop.Position.X > platform.Bounds.Right - 20);

        ClimbStep atTop = climb.Advance(topY, 0);
        AssertEx.Equal(ClimbOutcome.ReachedTop, atTop.Outcome);
        AssertEx.Near(platform.Bounds.Right - 20, atTop.Position.X);
    }

    /// <summary>The ledge blend slides the character a full body width off the wall's face
    /// while still climbing; renderers scale the climbing pose by this so the limbs stop
    /// reaching for a wall the body has already left.</summary>
    private static void WallClimbReportsWallContact()
    {
        var climb = new CharacterWallClimb();
        DesktopPlatform platform = Platform("window:1", 100, 200, 200);
        climb.Start(platform, WallSide.Right, new Size2(20, 40));
        double topY = platform.Segments[0].SurfaceY - 40;

        climb.Advance(topY, 40);
        AssertEx.Near(1, climb.WallContact);

        climb.Advance(topY, 6);
        AssertEx.True(climb.WallContact > 0);
        AssertEx.True(climb.WallContact < 1);

        climb.Advance(topY, 0);
        AssertEx.Near(0, climb.WallContact);

        // Letting go halfway up must leave the grip at full strength, so that climb still
        // fades out of the pose gracefully instead of snapping the arms down.
        climb.Advance(topY, 40);
        climb.Stop();
        AssertEx.Near(1, climb.WallContact);
    }

    /// <summary>The overlay paints above every real window, so clinging to an edge that another
    /// window covers reads as climbing thin air across the middle of whatever is on top.</summary>
    private static void WallEdgeCoveredByFrontWindowRejected()
    {
        DesktopPlatform wall = Platform("window:back", 400, 200, 300, zOrder: 5);
        DesktopPlatform front = Platform("window:front", 300, 150, 300, zOrder: 1);
        AssertEx.True(!WallEdgeVisibility.IsUsable(wall, wall.Bounds.X, 220, [wall, front]));
    }

    private static void WallEdgeUsableOutsideCoveringWindow()
    {
        DesktopPlatform wall = Platform("window:back", 400, 200, 300, zOrder: 5);
        DesktopPlatform front = Platform("window:front", 300, 150, 300, zOrder: 1);

        // Below the covering window's bottom edge, and behind a window that is further back.
        AssertEx.True(WallEdgeVisibility.IsUsable(wall, wall.Bounds.X, 280, [wall, front]));
        DesktopPlatform behind = front with { Id = "window:behind", ZOrder = 9 };
        AssertEx.True(WallEdgeVisibility.IsUsable(wall, wall.Bounds.X, 220, [wall, behind]));
    }

    private static void WallSideResolverOwnEdge()
    {
        AssertEx.Equal(WallSide.Right, WallSideResolver.ForOwnEdge(1));
        AssertEx.Equal(WallSide.Left, WallSideResolver.ForOwnEdge(-1));
    }

    private static void WallSideResolverEncounteredWall()
    {
        AssertEx.Equal(WallSide.Left, WallSideResolver.ForEncounteredWall(1));
        AssertEx.Equal(WallSide.Right, WallSideResolver.ForEncounteredWall(-1));
    }

    private static void WallEncounterFindsNeighborAhead()
    {
        DesktopPlatform floor = Platform("floor", 0, 100, 400);
        DesktopPlatform wall = Platform("wall", 300, 50, 40);
        var character = new RectD(250, 60, 20, 40);
        WallEncounterDetector.Neighbors neighbors = WallEncounterDetector.FindNeighborWalls(
            floor, character, [floor, wall]);
        AssertEx.True(neighbors.Right is not null);
        AssertEx.Equal("wall", neighbors.Right!.Value.Platform.Id);
        AssertEx.Near(300, neighbors.Right!.Value.Boundary);
        AssertEx.True(neighbors.Left is null);
    }

    private static void WallEncounterIgnoresCoveredWall()
    {
        DesktopPlatform floor = Platform("floor", 0, 100, 400);
        DesktopPlatform wall = Platform("wall", 300, 50, 40);
        var character = new RectD(250, 60, 20, 40);

        // Sits in front of the wall and covers its near edge at the character's own height, but
        // stops above the walking line so it is not itself a neighbour — otherwise this would
        // pass by finding the covering window instead of finding nothing.
        DesktopPlatform front = Platform("front", 280, 20, 60, zOrder: -1) with
        {
            Bounds = new RectD(280, 20, 60, 70),
        };

        WallEncounterDetector.Neighbors neighbors = WallEncounterDetector.FindNeighborWalls(
            floor, character, [floor, wall, front]);
        AssertEx.True(neighbors.Right is null);
    }

    private static void WallEncounterIgnoresNonCrossingWindow()
    {
        DesktopPlatform floor = Platform("floor", 0, 100, 400);
        DesktopPlatform farBelow = Platform("far", 300, 500, 40);
        var character = new RectD(250, 60, 20, 40);
        WallEncounterDetector.Neighbors neighbors = WallEncounterDetector.FindNeighborWalls(
            floor, character, [floor, farBelow]);
        AssertEx.True(neighbors.Right is null);
        AssertEx.True(neighbors.Left is null);
    }

    private static void WallGrabFindsEdgeInReach()
    {
        DesktopPlatform platform = Platform("window:1", 300, 50, 200);
        var character = new RectD(270, 80, 20, 40);
        WallGrabDetector.Reach? reach = WallGrabDetector.FindReachableEdge(character, 400, [platform], 16);
        AssertEx.True(reach is not null);
        AssertEx.Equal("window:1", reach!.Value.Platform.Id);
        AssertEx.Equal(WallSide.Left, reach!.Value.Side);
    }

    private static void WallGrabIgnoresOutOfReach()
    {
        DesktopPlatform platform = Platform("window:1", 300, 50, 200);
        var character = new RectD(200, 80, 20, 40);
        AssertEx.True(WallGrabDetector.FindReachableEdge(character, 400, [platform], 16) is null);
    }

    private static void WallGrabIgnoresStationaryCharacter()
    {
        DesktopPlatform platform = Platform("window:1", 300, 50, 200);
        var character = new RectD(270, 80, 20, 40);
        AssertEx.True(WallGrabDetector.FindReachableEdge(character, 0, [platform], 16) is null);
    }

    private static void IntegrateReportsRightEdgeApproachDirection()
    {
        // Integrate's own bounce-turnaround flips WalkDirection to the opposite sign on the
        // very same call that reports the edge hit (so free autonomous wandering turns
        // around), which means a caller reading WalkDirection afterward to decide which
        // wall/edge was just hit would always see the wrong (opposite) side — this was the
        // root cause of the character teleporting to the far edge when a wall/climb reaction
        // picked its side that way. HitEdgeDirection preserves the real approach direction.
        var physics = new CharacterPhysics(new Vec2(190, 0), new Size2(20, 40));
        CharacterMotionStep step = physics.Integrate(1, CharacterState.Walk, 0, 200);
        AssertEx.True(step.HitHorizontalEdge);
        AssertEx.Equal(1, step.HitEdgeDirection);
        AssertEx.Equal(-1, physics.WalkDirection);
    }

    private static void IntegrateReportsLeftEdgeApproachDirection()
    {
        var physics = new CharacterPhysics(new Vec2(-5, 0), new Size2(20, 40));
        physics.FaceDirection(-1);
        CharacterMotionStep step = physics.Integrate(1, CharacterState.Walk, 0, 200);
        AssertEx.True(step.HitHorizontalEdge);
        AssertEx.Equal(-1, step.HitEdgeDirection);
        AssertEx.Equal(1, physics.WalkDirection);
    }

    private static void IntegrateReportsNoEdgeDirectionMidFloor()
    {
        var physics = new CharacterPhysics(new Vec2(100, 0), new Size2(20, 40));
        CharacterMotionStep step = physics.Integrate(0.016, CharacterState.Walk, 0, 200);
        AssertEx.False(step.HitHorizontalEdge);
        AssertEx.Equal(0, step.HitEdgeDirection);
    }

    private static void WallClimbRetargetsToMovedPlatform()
    {
        // A window dragged mid-climb must not leave the character clinging to wherever it
        // used to be — Retarget() re-syncs the wall geometry to the platform's current
        // bounds so the very next Advance() reflects the move.
        var climb = new CharacterWallClimb();
        DesktopPlatform platform = Platform("window:1", 100, 200, 200);
        climb.Start(platform, WallSide.Right, new Size2(20, 40));
        double topY = platform.Segments[0].SurfaceY - 40;
        ClimbStep beforeMove = climb.Advance(topY, 40);
        AssertEx.Near(platform.Bounds.Right, beforeMove.Position.X);

        DesktopPlatform moved = Platform("window:1", 250, 200, 200);
        climb.Retarget(moved, new Size2(20, 40));
        ClimbStep afterMove = climb.Advance(topY, 40);
        AssertEx.Near(moved.Bounds.Right, afterMove.Position.X);
        AssertEx.True(Math.Abs(afterMove.Position.X - beforeMove.Position.X) > 1);
    }

    private static void WallClimbClampsHorizontallyToScreenBounds()
    {
        // A window hanging mostly off the right side of a narrow screen must not let the
        // character climb out into the off-screen portion — Start()/Retarget() clamp both
        // the clinging (wall-face) and ledge (top-of-climb) X to the given screen bounds
        // instead of following the platform's real (partly off-screen) geometry verbatim.
        var climb = new CharacterWallClimb();
        DesktopPlatform platform = Platform("window:1", 800, 100, 200);
        var screenBounds = new RectD(0, 0, 500, 600);
        climb.Start(platform, WallSide.Left, new Size2(20, 40), screenBounds);
        double topY = platform.Segments[0].SurfaceY - 40;

        ClimbStep clinging = climb.Advance(topY, 40);
        AssertEx.True(clinging.Position.X <= (screenBounds.Right - 20) + 0.01);

        ClimbStep atTop = climb.Advance(topY, 0);
        AssertEx.Equal(ClimbOutcome.ReachedTop, atTop.Outcome);
        AssertEx.True(atTop.Position.X <= (screenBounds.Right - 20) + 0.01);
    }

    private static void WallClimbClampsBottomToScreenFloor()
    {
        // A window taller than the monitor (extending well below the visible work area)
        // must not let the character climb down past the real screen floor, the same way
        // the horizontal wall face is clamped to the screen's sides.
        DesktopPlatform platform = Platform("window:1", 100, 50, 200) with
        {
            Bounds = new RectD(100, 50, 200, 800),
        };
        var screenBounds = new RectD(0, 0, 500, 300);
        var climb = new CharacterWallClimb();
        climb.Start(platform, WallSide.Right, new Size2(20, 40), screenBounds);

        ClimbStep step = climb.Advance(100, 100_000);
        AssertEx.Equal(ClimbOutcome.ReachedBottom, step.Outcome);
        AssertEx.Near(screenBounds.Bottom - 40, step.Position.Y);
    }

    private static void WallGrabAcceptsScreenEdgePlatform()
    {
        DesktopPlatform edge = Platform("screen:left-edge", 300, 50, 2) with { Kind = PlatformKind.ScreenEdge };
        var character = new RectD(270, 80, 20, 40);
        WallGrabDetector.Reach? reach = WallGrabDetector.FindReachableEdge(character, 400, [edge], 16);
        AssertEx.True(reach is not null);
        AssertEx.Equal("screen:left-edge", reach!.Value.Platform.Id);
    }

    private static void WallGrabIgnoresDesktopPlatform()
    {
        DesktopPlatform floor = Platform("desktop:work-area", 300, 50, 200) with { Kind = PlatformKind.Desktop };
        var character = new RectD(270, 80, 20, 40);
        AssertEx.True(WallGrabDetector.FindReachableEdge(character, 400, [floor], 16) is null);
    }

    private static void ClimbToTopReattachesWithoutThrowing()
    {
        DesktopPlatform platform = Platform("window:1", 100, 200, 200);
        var size = new Size2(20, 40);
        var climb = new CharacterWallClimb();

        foreach (WallSide side in new[] { WallSide.Left, WallSide.Right })
        {
            climb.Start(platform, side, size);
            double startY = platform.Bounds.Bottom - size.Height;
            ClimbStep step = climb.Advance(startY, -1_000);
            AssertEx.Equal(ClimbOutcome.ReachedTop, step.Outcome);

            // Advance() itself now lands ReachedTop flush on the ledge (see
            // WallClimbBlendsOntoLedgeNearTop) — attaching at that exact position must not
            // throw the way it used to when it still clung to the outer wall face, outside
            // the platform's own surface segment.
            var bounds = new RectD(step.Position.X, step.Position.Y, size.Width, size.Height);
            var attachment = new CharacterPlatformAttachment();
            attachment.Attach(platform, bounds);
            AssertEx.True(attachment.IsAttached);
        }
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
