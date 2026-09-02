using System.Collections.Generic;
using System.Linq;
using HollowLines.Core;
using NUnit.Framework;
using System.Diagnostics;

// EditMode tests — run via Window > General > Test Runner.
// Same coverage as the standalone console harness that validated the core.
namespace HollowLines.Tests
{
    internal static class TestUtil
    {
        public static void TickMany(GravitySystem gravity, float seconds, GridPos avatar)
        {
            const float step = 1f / 60f;
            for (float t = 0f; t < seconds; t += step)
                gravity.Tick(step, avatar);
        }
    }

    [TestFixture]
    public class GridModelTests
    {
        [Test]
        public void Drill_RemovesSolid_AndFiresEvent()
        {
            var grid = GridModel.FromStringMap(new[] { "A" });
            GridPos? changed = null;
            grid.CellChanged += (p, t) => { if (t == CellType.Empty) changed = p; };

            Assert.IsTrue(grid.Drill(new GridPos(0, 0)));
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 0)));
            Assert.AreEqual(new GridPos(0, 0), changed);
        }

        [Test]
        public void Drill_OnEmptyOrOutOfBounds_ReturnsFalse()
        {
            var grid = GridModel.FromStringMap(new[] { ".A" });
            Assert.IsFalse(grid.Drill(new GridPos(0, 0)));
            Assert.IsFalse(grid.Drill(new GridPos(5, 5)));
        }

        [Test]
        public void IsRowVoid_DetectsFullVoidRow()
        {
            var grid = GridModel.FromStringMap(new[] { "A.A", "..." });
            Assert.IsFalse(grid.IsRowVoid(0));
            Assert.IsTrue(grid.IsRowVoid(1));
        }

        [Test]
        public void Constructor_SetsWidthAndHeight_DefaultV3Width()
        {
            var grid = new GridModel(width: 7, height: 14);
            Assert.AreEqual(7, grid.Width);
            Assert.AreEqual(14, grid.Height);
        }

        [Test]
        public void FromStringMap_InfersWidthFromFirstRow()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                "AAB.CC.",
                ".......",
            });
            Assert.AreEqual(7, grid.Width);
            Assert.AreEqual(2, grid.Height);
            Assert.IsTrue(grid.IsRowVoid(1));
            Assert.IsFalse(grid.IsRowVoid(0));
        }

        [Test]
        public void FromStringMap_RowLengthMismatch_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => GridModel.FromStringMap(new[]
            {
                "AAB.CC.",
                "AA.",
            }));
        }

        [Test]
        public void InBounds_RespectsParameterizedWidth_NotHardcodedTen()
        {
            var grid = new GridModel(width: 7, height: 3);
            Assert.IsTrue(grid.InBounds(new GridPos(6, 0)), "last valid column at width 7");
            Assert.IsFalse(grid.InBounds(new GridPos(7, 0)), "one past width 7");
            Assert.IsFalse(grid.InBounds(new GridPos(9, 0)), "old width-10 column must be out of bounds now");
        }
    }

    [TestFixture]
    public class ChunkSystemTests
    {
        [Test]
        public void SameColorOrthogonal_Fuse()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                "A..",
                "AA.",
                "..B"
            });
            List<Chunk> chunks = ChunkSystem.ComputeChunks(grid);
            Assert.AreEqual(2, chunks.Count);
            Assert.AreEqual(3, chunks.First(c => c.Color == CellType.ColorA).Cells.Count);
        }

        [Test]
        public void DiagonalOrDifferentColor_DoNotFuse()
        {
            var diagonal = GridModel.FromStringMap(new[] { "A.", ".A" });
            Assert.AreEqual(2, ChunkSystem.ComputeChunks(diagonal).Count);

            var mixed = GridModel.FromStringMap(new[] { "AB" });
            Assert.AreEqual(2, ChunkSystem.ComputeChunks(mixed).Count);
        }

        [Test]
        public void DrillingConnectingCell_SplitsChunk()
        {
            var grid = GridModel.FromStringMap(new[] { "AAA" });
            Assert.AreEqual(1, ChunkSystem.ComputeChunks(grid).Count);

            grid.Drill(new GridPos(1, 0));
            Assert.AreEqual(2, ChunkSystem.ComputeChunks(grid).Count);
        }
    }

    [TestFixture]
    public class GravitySystemTests
    {
        [Test]
        public void Bridge_SupportedAtBothEnds_DoesNotWobble()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                "AAA",
                "B.B"
            });
            var gravity = new GravitySystem(grid);
            bool wobbled = false;
            gravity.WobbleStarted += _ => wobbled = true;

            TestUtil.TickMany(gravity, 2f, new GridPos(1, 1));
            Assert.IsFalse(wobbled);
            Assert.AreEqual(CellType.ColorA, grid.Get(new GridPos(1, 0)));
        }

        [Test]
        public void Bridge_LosingBothSupports_FallsAsOneSlab()
        {
            // One-row drop on purpose: this test is about slab cohesion, not the v3 burst
            // threshold (2+ rows). ChunkBurstTests owns the shatter cases.
            var grid = GridModel.FromStringMap(new[]
            {
                "AAA",
                "B.B"
            });
            var gravity = new GravitySystem(grid);
            grid.Drill(new GridPos(0, 1));
            TestUtil.TickMany(gravity, 2f, new GridPos(1, 1));
            Assert.AreEqual(CellType.ColorA, grid.Get(new GridPos(0, 0)), "bridge holds on one pillar");

            grid.Drill(new GridPos(2, 1));
            TestUtil.TickMany(gravity, 2f, new GridPos(1, 0));
            Assert.AreEqual(CellType.ColorA, grid.Get(new GridPos(0, 1)));
            Assert.AreEqual(CellType.ColorA, grid.Get(new GridPos(1, 1)));
            Assert.AreEqual(CellType.ColorA, grid.Get(new GridPos(2, 1)));
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 0)));
        }

        [Test]
        public void UnsupportedChunk_WobblesThenFallsThenLands()
        {
            var grid = GridModel.FromStringMap(new[] { "A", ".", "." });
            var gravity = new GravitySystem(grid) { WobbleDuration = 0.6f, FallStepInterval = 0.08f };
            bool wobbled = false, landed = false;
            int moves = 0;
            gravity.WobbleStarted += _ => wobbled = true;
            gravity.ChunkMoved += _ => moves++;
            gravity.ChunkLanded += _ => landed = true;

            var avatar = new GridPos(0, 0);
            TestUtil.TickMany(gravity, 0.5f, avatar);
            Assert.IsTrue(wobbled);
            Assert.AreEqual(CellType.ColorA, grid.Get(new GridPos(0, 0)), "no movement during the telegraph");
            Assert.IsTrue(gravity.IsCellWobbling(new GridPos(0, 0)));

            TestUtil.TickMany(gravity, 2f, avatar);
            Assert.AreEqual(2, moves);
            Assert.IsTrue(landed);
            // v3: a 2-row drop lands AND shatters, so the cell it landed in is cleared.
            // ChunkLanded still fires first — see ChunkBurstTests for the burst contract.
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 2)), "the landing chunk burst on impact");
        }

        [Test]
        public void StackedChunks_FallAndLandStacked()
        {
            // One-row drop: stacking order is the subject here, not the v3 burst threshold.
            var grid = GridModel.FromStringMap(new[] { "A", "B", "." });
            var gravity = new GravitySystem(grid);
            TestUtil.TickMany(gravity, 3f, new GridPos(0, 0));
            Assert.AreEqual(CellType.ColorB, grid.Get(new GridPos(0, 2)));
            Assert.AreEqual(CellType.ColorA, grid.Get(new GridPos(0, 1)));
        }

        [Test]
        public void FallingChunk_OntoAvatar_FiresCrush()
        {
            // One-row drop so the block survives the landing — CrushFiresBeforeBurst covers
            // the v3 case where the same impact also shatters.
            var grid = GridModel.FromStringMap(new[] { "A", "." });
            var gravity = new GravitySystem(grid);
            var avatarCell = new GridPos(0, 1);
            var crushes = new List<GridPos>();
            gravity.AvatarCrushed += p => crushes.Add(p);

            TestUtil.TickMany(gravity, 3f, avatarCell);
            Assert.IsNotEmpty(crushes);
            Assert.AreEqual(avatarCell, crushes[0]);
            Assert.AreEqual(CellType.ColorA, grid.Get(avatarCell), "the avatar never supports a block");
        }
    }

    [TestFixture]
    public class ChunkBurstTests
    {
        // Fast telegraph/fall so a whole burst cascade fits in a short tick budget.
        private static GravitySystem FastGravity(GridModel grid) =>
            new GravitySystem(grid) { WobbleDuration = 0.1f, FallStepInterval = 0.02f };

        // ── Fall distance threshold ─────────────────────────────────────────

        [Test]
        public void FallOfOneRow_DoesNotBurst()
        {
            var grid    = GridModel.FromStringMap(new[] { "B", ".", "A" });
            var gravity = FastGravity(grid);
            bool burst  = false;
            gravity.ChunkBurst += (_, __, ___) => burst = true;

            TestUtil.TickMany(gravity, 2f, new GridPos(0, 0));

            Assert.IsFalse(burst, "a 1-row drop must land intact");
            Assert.AreEqual(CellType.ColorB, grid.Get(new GridPos(0, 1)), "chunk survives the landing");
        }

        [Test]
        public void FallOfTwoRows_Bursts_AndClearsEveryChunkCell()
        {
            var grid    = GridModel.FromStringMap(new[] { "B", ".", ".", "A" });
            var gravity = FastGravity(grid);
            List<GridPos> burstCells = null;
            int distance = -1;
            gravity.ChunkBurst += (cells, d, _) => { burstCells = cells; distance = d; };

            TestUtil.TickMany(gravity, 2f, new GridPos(0, 0));

            Assert.IsNotNull(burstCells, "a 2-row drop must shatter");
            Assert.AreEqual(2, distance);
            CollectionAssert.AreEqual(new[] { new GridPos(0, 2) }, burstCells);
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 2)), "burst cells are cleared");
        }

        [Test]
        public void ChunkBurst_ReportsTheChunkColor_AfterCellsAreCleared()
        {
            // The view tints debris with this; the cells are already Empty when the event fires,
            // so the color cannot be read back off the grid.
            var grid    = GridModel.FromStringMap(new[] { "B", ".", ".", "A" });
            var gravity = FastGravity(grid);
            CellType reported = CellType.Empty;
            gravity.ChunkBurst += (_, __, color) => reported = color;

            TestUtil.TickMany(gravity, 2f, new GridPos(0, 0));

            Assert.AreEqual(CellType.ColorB, reported);
        }

        [Test]
        public void FallOfFourRows_ReportsFallDistanceFour()
        {
            var grid    = GridModel.FromStringMap(new[] { "B", ".", ".", ".", ".", "A" });
            var gravity = FastGravity(grid);
            int distance = -1;
            gravity.ChunkBurst += (_, d, __) => distance = d;

            TestUtil.TickMany(gravity, 3f, new GridPos(0, 0));

            Assert.AreEqual(4, distance);
        }

        // ── Shockwave effects ────────────────────────────────────────────────

        [Test]
        public void Shockwave_DestroysAdjacentColorAndHard()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                "BB.",
                "...",
                "...",
                "AHC"
            });
            var gravity = FastGravity(grid);

            TestUtil.TickMany(gravity, 2f, new GridPos(2, 0));

            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 3)), "color under the burst is destroyed");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 3)), "hard under the burst is destroyed");
            Assert.AreEqual(CellType.ColorC, grid.Get(new GridPos(2, 3)), "diagonal neighbor is outside the 1-cell ring");
        }

        [Test]
        public void Shockwave_SoftensSteelToHard()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                "BB.",
                "...",
                "...",
                "ASC"
            });
            var gravity = FastGravity(grid);

            TestUtil.TickMany(gravity, 2f, new GridPos(2, 0));

            Assert.AreEqual(CellType.Hard, grid.Get(new GridPos(1, 3)), "steel is softened, never destroyed outright");
        }

        [Test]
        public void Shockwave_LiberatesAirCapsule()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                "BB.",
                "...",
                "...",
                "APC"
            });
            var gravity = FastGravity(grid);
            GridPos? liberated = null;
            gravity.AirCapsuleLiberated += p => liberated = p;

            TestUtil.TickMany(gravity, 2f, new GridPos(2, 0));

            Assert.AreEqual(new GridPos(1, 3), liberated, "capsule must be liberated, not silently destroyed");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 3)));
        }

        [Test]
        public void Shockwave_LiberatesDiamond()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                "BB.",
                "...",
                "...",
                "ADC"
            });
            var gravity = FastGravity(grid);
            GridPos? liberated = null;
            gravity.DiamondLiberated += p => liberated = p;

            TestUtil.TickMany(gravity, 2f, new GridPos(2, 0));

            Assert.AreEqual(new GridPos(1, 3), liberated, "diamond must be liberated, not silently destroyed");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 3)));
        }

        [Test]
        public void Shockwave_ArmsAdjacentBomb_AndLeavesItOnTheGrid()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                "BB.",
                "...",
                "...",
                "AXC"
            });
            var gravity = FastGravity(grid);
            GridPos? armed = null;
            gravity.BombArmedByBurst += p => armed = p;

            TestUtil.TickMany(gravity, 2f, new GridPos(2, 0));

            Assert.AreEqual(new GridPos(1, 3), armed);
            Assert.AreEqual(CellType.Bomb, grid.Get(new GridPos(1, 3)), "the bomb's own fuse resolves it, not the shockwave");
        }

        [Test]
        public void Avatar_InShockwaveRing_IsNotHit()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                "B..",
                "...",
                "...",
                "A.."
            });
            var gravity = FastGravity(grid);
            bool hit = false;
            gravity.AvatarHitByBurst += () => hit = true;

            // (1,2) is the cardinal right neighbor of the landing cell (0,2): inside the shockwave
            // ring but outside the chunk footprint. §5.2 dev 5: the ring no longer harms the avatar,
            // so a single sidestep out of the footprint is a clean dodge (design rule 4).
            TestUtil.TickMany(gravity, 2f, new GridPos(1, 2));

            Assert.IsFalse(hit, "the shockwave ring is harmless to the avatar — only the footprint crushes");
        }

        [Test]
        public void Avatar_OutsideShockwaveZone_IsNotHit()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                "B..",
                "...",
                "...",
                "A.."
            });
            var gravity = FastGravity(grid);
            bool hit = false;
            gravity.AvatarHitByBurst += () => hit = true;

            TestUtil.TickMany(gravity, 2f, new GridPos(2, 0));

            Assert.IsFalse(hit, "two cells away is outside the 1-cell ring");
        }

        // ── Ordering ─────────────────────────────────────────────────────────

        [Test]
        public void CrushFiresBeforeBurst_WhenAvatarIsUnderTheLandingChunk()
        {
            var grid    = GridModel.FromStringMap(new[] { "B", ".", ".", "A" });
            var gravity = FastGravity(grid);
            var order   = new List<string>();
            gravity.AvatarCrushed   += _      => order.Add("crush");
            gravity.ChunkBurst      += (_, __, ___) => order.Add("burst");
            gravity.AvatarHitByBurst += ()    => order.Add("burstHit");

            TestUtil.TickMany(gravity, 2f, new GridPos(0, 2));

            Assert.AreEqual("crush", order[0], "the fall crushes before the impact shatters");
            Assert.Contains("burst", order, "the chunk still bursts after crushing");
            Assert.Less(order.IndexOf("crush"), order.IndexOf("burst"));
            Assert.Contains("burstHit", order, "a crushed avatar stands inside the burst footprint");
        }

        // ── Cascade ──────────────────────────────────────────────────────────

        [Test]
        public void BurstCascade_ShockwaveUnderminesSecondChunk_WhichBurstsToo()
        {
            // B drops 2 rows onto H and bursts. Its shockwave eats the 'A' keystone at (1,2),
            // which was the only thing holding C at (1,1). C then free-falls 6 rows and bursts too.
            var grid = GridModel.FromStringMap(new[]
            {
                "B...",
                ".C..",
                ".AA.",
                "H.A.",
                "H.A.",
                "H.A.",
                "H.A.",
                "H.A.",
                "AAAA"
            });
            var gravity   = FastGravity(grid);
            var distances = new List<int>();
            gravity.ChunkBurst += (_, d, __) => distances.Add(d);

            TestUtil.TickMany(gravity, 6f, new GridPos(3, 0));

            Assert.AreEqual(2, distances.Count, "the first burst must undermine the second chunk");
            Assert.AreEqual(2, distances[0], "B fell 2 rows");
            Assert.AreEqual(6, distances[1], "C fell 6 rows after losing its keystone");
        }

        // ── Settle (board load) ──────────────────────────────────────────────

        [Test]
        public void Settle_DropsUnsupportedChunk_WithoutBursting()
        {
            // B would fall 3 rows to the floor — well past the burst threshold — but Settle() must
            // never burst: it drops the generated board into a rest state, not a demolition (R2.8d).
            var grid = GridModel.FromStringMap(new[] { "B", ".", ".", "." });
            var gravity = new GravitySystem(grid);
            bool burst = false;
            gravity.ChunkBurst += (_, __, ___) => burst = true;

            gravity.Settle();

            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 0)), "B left the top");
            Assert.AreEqual(CellType.ColorB, grid.Get(new GridPos(0, 3)), "B settled on the floor");
            Assert.IsFalse(burst, "settling never bursts, however far a chunk falls");
        }

        [Test]
        public void Settle_LeavesBoardAtRest_NoWobbleOrBurstOnNextTick()
        {
            var grid = GridModel.FromStringMap(new[] { "B", ".", ".", "." });
            var gravity = new GravitySystem(grid);
            gravity.Settle();

            bool anything = false;
            gravity.WobbleStarted += _        => anything = true;
            gravity.ChunkMoved    += _        => anything = true;
            gravity.ChunkBurst    += (_, __, ___) => anything = true;

            gravity.Tick(1f, new GridPos(0, 0));

            Assert.IsFalse(anything, "a settled board is stable — nothing wobbles, moves or bursts on the first tick");
            Assert.IsFalse(gravity.IsBusy);
        }

        [Test]
        public void Settle_StacksFallingChunks_WithoutOverlap()
        {
            // Two separate chunks over empty space settle into a clean stack on the floor.
            var grid = GridModel.FromStringMap(new[] { "B", "C", ".", ".", "A" });
            var gravity = new GravitySystem(grid);

            gravity.Settle();

            Assert.AreEqual(CellType.ColorA, grid.Get(new GridPos(0, 4)), "floor unchanged");
            Assert.AreEqual(CellType.ColorC, grid.Get(new GridPos(0, 3)), "C rests on the floor block");
            Assert.AreEqual(CellType.ColorB, grid.Get(new GridPos(0, 2)), "B rests on C — no overlap");
            Assert.AreEqual(CellType.Empty,  grid.Get(new GridPos(0, 1)));
            Assert.AreEqual(CellType.Empty,  grid.Get(new GridPos(0, 0)));
        }
    }

    [TestFixture]
    public class AvatarModelTests
    {
        [Test]
        public void WalksAndStepsUp()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                "...",
                "..A",
                "AAA"
            });
            var avatar = new AvatarModel(grid, new GridPos(0, 1));

            Assert.IsTrue(avatar.TryMove(1));
            Assert.AreEqual(new GridPos(1, 1), avatar.Position);

            Assert.IsTrue(avatar.TryMove(1), "single-block step-up");
            Assert.AreEqual(new GridPos(2, 0), avatar.Position);

            Assert.IsFalse(avatar.TryMove(1), "edge of the well");
        }

        [Test]
        public void FallsWhenUnsupported()
        {
            var grid = GridModel.FromStringMap(new[] { ".", ".", "A" });
            var avatar = new AvatarModel(grid, new GridPos(0, 0)) { FallStepInterval = 0.06f };
            for (int i = 0; i < 60; i++)
                avatar.Tick(1f / 60f);
            Assert.AreEqual(new GridPos(0, 1), avatar.Position);
            Assert.IsFalse(avatar.IsFalling);
        }

        [Test]
        public void DrillUp_FreesBlockAbove_KeystoneScenario()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                "B.",
                "A.",
                "..",
                "AA"
            });
            var avatar = new AvatarModel(grid, new GridPos(0, 2));
            var gravity = new GravitySystem(grid);
            bool crushed = false;
            bool burstHit = false;
            gravity.AvatarCrushed    += _  => crushed = true;
            gravity.AvatarHitByBurst += () => burstHit = true;

            Assert.IsTrue(avatar.TryDrill(0, -1));
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 1)));

            TestUtil.TickMany(gravity, 0.3f, avatar.Position);
            Assert.AreEqual(CellType.ColorB, grid.Get(new GridPos(0, 0)), "still telegraphing");
            Assert.IsTrue(avatar.TryMove(1), "sidestep during the wobble window");

            for (int i = 0; i < 240; i++)
            {
                avatar.Tick(1f / 60f);
                gravity.Tick(1f / 60f, avatar.Position);
            }
            Assert.IsFalse(crushed, "dodged the crush in time");
            // v3: the freed block drops 2 rows, so it shatters instead of resting in the dug column.
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 2)), "the freed block burst on impact");
            // §5.2 dev 5 resolved: one sidestep out of the footprint is now a clean dodge — the
            // shockwave ring is harmless to the avatar, so a correct dodge is never punished (rule 4).
            Assert.IsFalse(burstHit, "the adjacent dodge cell is only in the ring, which no longer hits the avatar");
        }

        // ── Coyote time: a brief grace window to catch a ledge you just walked off ──

        [Test]
        public void Move_DuringCoyoteWindow_CatchesAdjacentLedge()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                ".....",
                ".....",
                ".....",
                ".H...", // ledge at col 1
                "AAAAA"
            });
            var avatar = new AvatarModel(grid, new GridPos(2, 2)) { CoyoteTime = 0.12f, FallStepInterval = 0.06f };

            Assert.IsTrue(avatar.IsFalling, "below is empty → falling");
            Assert.IsTrue(avatar.InCoyoteWindow, "the grace window is open the instant the fall begins");

            Assert.IsTrue(avatar.TryMove(-1), "a sideways step is allowed during coyote");
            Assert.AreEqual(new GridPos(1, 2), avatar.Position);
            Assert.IsFalse(avatar.IsFalling, "landed on the ledge (col 1 is solid below)");
        }

        [Test]
        public void Move_AfterCoyoteWindow_IsLockedMidFall()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                ".....",
                ".....",
                ".....",
                ".....", // left of the falling column stays empty, so only the LOCK can block the move
                ".....",
                "AAAAA"
            });
            var avatar = new AvatarModel(grid, new GridPos(2, 0)) { CoyoteTime = 0.12f, FallStepInterval = 0.06f };

            avatar.Tick(0.20f); // 0.20 s > 0.12 s → the grace window has closed

            Assert.IsTrue(avatar.IsFalling, "still in the air");
            Assert.IsFalse(avatar.InCoyoteWindow, "grace window is over");
            Assert.IsFalse(avatar.TryMove(-1), "committed fall: horizontal control is locked");
            Assert.AreEqual(new GridPos(2, 3), avatar.Position, "the blocked move did not shift the avatar");
        }

        [Test]
        public void Move_DuringCoyote_NeverStepsUpMidAir()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                ".....",
                ".....", // (1,1) and (2,1) free → step-up geometry is satisfied…
                ".H...", // …but (1,2) is the solid we'd have to climb
                ".....", // below the avatar is empty → falling
                "AAAAA"
            });
            var avatar = new AvatarModel(grid, new GridPos(2, 2)) { CoyoteTime = 0.12f };

            Assert.IsTrue(avatar.InCoyoteWindow);
            Assert.IsFalse(avatar.TryMove(-1), "step-up must not work mid-air, even during coyote");
            Assert.AreEqual(new GridPos(2, 2), avatar.Position, "avatar did not climb");
        }

        [Test]
        public void Coyote_DoesNotReExtend_WhenDriftingSideways()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                ".....",
                ".....",
                ".....",
                ".....",
                "AAAAA"
            });
            var avatar = new AvatarModel(grid, new GridPos(2, 0)) { CoyoteTime = 0.12f, FallStepInterval = 0.06f };

            avatar.Tick(0.08f); // 0.08 s into the fall — window still open
            Assert.IsTrue(avatar.InCoyoteWindow);
            Assert.IsTrue(avatar.TryMove(-1), "drift sideways while still in grace");

            // The drift must NOT reset the fall clock: 0.08 + 0.06 = 0.14 s > 0.12 s → window closes.
            avatar.Tick(0.06f);
            Assert.IsFalse(avatar.InCoyoteWindow, "the sideways step did not recharge the grace window");
            Assert.IsFalse(avatar.TryMove(1), "fall is now committed");
        }

        [Test]
        public void Drill_StaysLocked_DuringCoyote()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                ".....",
                ".....",
                ".C...", // a drillable block beside the avatar
                ".....", // below is empty → falling
                "AAAAA"
            });
            var avatar = new AvatarModel(grid, new GridPos(2, 2)) { CoyoteTime = 0.12f };

            Assert.IsTrue(avatar.InCoyoteWindow, "coyote is for movement only");
            Assert.IsFalse(avatar.TryDrill(-1, 0), "drilling stays locked while falling");
            Assert.AreEqual(CellType.ColorC, grid.Get(new GridPos(1, 2)), "the block was not drilled");
        }
    }

    [TestFixture]
    public class AirSystemTests
    {
        private const float Delta = 0.001f;

        [Test]
        public void StartsAtFullAir()
        {
            var air = new AirSystem();
            Assert.AreEqual(AirSystem.MaxAir, air.Air, Delta);
            Assert.IsFalse(air.IsEmpty);
        }

        [Test]
        public void Tick_DrainsAtCorrectRate()
        {
            var air = new AirSystem();
            air.Tick(1f);
            Assert.AreEqual(AirSystem.MaxAir - AirSystem.DefaultDrainRate, air.Air, Delta);
        }

        [Test]
        public void Tick_UsesCustomDrainRate()
        {
            var air = new AirSystem { DrainRate = 3f };
            air.Tick(2f);
            Assert.AreEqual(AirSystem.MaxAir - 6f, air.Air, Delta);
        }

        [Test]
        public void Tick_FiresAirChanged()
        {
            var air = new AirSystem();
            float? reported = null;
            air.AirChanged += v => reported = v;

            air.Tick(1f);

            Assert.IsNotNull(reported);
            Assert.AreEqual(air.Air, reported.Value, Delta);
        }

        [Test]
        public void Tick_ClampsAtZero_NeverNegative()
        {
            var air = new AirSystem();
            // drain for far longer than it takes to empty
            for (int i = 0; i < 1000; i++)
                air.Tick(1f);

            Assert.AreEqual(0f, air.Air, Delta);
            Assert.IsTrue(air.IsEmpty);
        }

        [Test]
        public void AirDepleted_FiresExactlyOnce()
        {
            var air = new AirSystem();
            int count = 0;
            air.AirDepleted += () => count++;

            for (int i = 0; i < 1000; i++)
                air.Tick(1f);

            Assert.AreEqual(1, count, "AirDepleted must fire exactly once per run");
        }

        [Test]
        public void Tick_StopsChangingAfterEmpty()
        {
            var air = new AirSystem();
            for (int i = 0; i < 1000; i++)
                air.Tick(1f);

            int extraChanges = 0;
            air.AirChanged += _ => extraChanges++;

            air.Tick(1f);
            air.Tick(1f);

            Assert.AreEqual(0, extraChanges, "no AirChanged events once already at 0");
        }

        [Test]
        public void RestorePerfectClear_AddsCorrectAmount()
        {
            var air = new AirSystem();
            air.Tick(10f); // drain 50 % (DrainRate=5, 10s)
            float before = air.Air;

            air.RestorePerfectClear();

            Assert.AreEqual(before + AirSystem.PerfectClearRestoreAmount, air.Air, Delta);
        }

        [Test]
        public void RestorePerfectClear_ClampsAtMax()
        {
            var air = new AirSystem();
            // already full — restore should do nothing
            air.RestorePerfectClear();
            Assert.AreEqual(AirSystem.MaxAir, air.Air, Delta);
        }

        [Test]
        public void RestoreCapsule_AddsCorrectAmount()
        {
            var air = new AirSystem();
            air.Tick(10f); // drain 50 %
            float before = air.Air;

            air.RestoreCapsule();

            Assert.AreEqual(before + AirSystem.CapsuleRestoreAmount, air.Air, Delta);
        }

        [Test]
        public void RestoreCapsule_ClampsAtMax()
        {
            var air = new AirSystem();
            air.Tick(1f); // drain just 5 %
            air.RestoreCapsule(); // +25 % would overfill — must clamp

            Assert.AreEqual(AirSystem.MaxAir, air.Air, Delta);
        }

        [Test]
        public void RestorePerfectClear_FiresAirChanged()
        {
            var air = new AirSystem();
            air.Tick(10f); // drain so restore has room
            float? reported = null;
            air.AirChanged += v => reported = v;

            air.RestorePerfectClear();

            Assert.IsNotNull(reported);
            Assert.AreEqual(air.Air, reported.Value, Delta);
        }

        // ── v3 restore sources: burst + bomb chain ───────────────────────────

        [Test]
        public void RestoreBurst_AddsItsConfiguredAmount()
        {
            var air = new AirSystem();
            air.Tick(10f); // drain to 50 %
            Assert.AreEqual(50f, air.Air, Delta, "precondition");

            air.RestoreBurst();

            Assert.AreEqual(50f + AirSystem.BurstRestoreAmount, air.Air, Delta);
        }

        [Test]
        public void RestoreBurst_ClampsAtMax()
        {
            var air = new AirSystem();
            air.Tick(0.05f);    // drain a sliver
            air.RestoreBurst(); // would overfill

            Assert.AreEqual(AirSystem.MaxAir, air.Air, Delta);
        }

        [Test]
        public void RestoreBombChain_ScalesWithBombCount()
        {
            var air = new AirSystem();
            air.Tick(10f); // drain to 50 %

            air.RestoreBombChain(3);

            Assert.AreEqual(50f + 3 * AirSystem.BombChainRestorePerBomb, air.Air, Delta);
        }

        [Test]
        public void RestoreEconomy_IsSmallRelativeToDrain()
        {
            // §15.1 regression guard: these were 20× oversupplied and made the air clock
            // decorative. A single burst must never be worth more than a second of drain.
            Assert.Less(AirSystem.BurstRestoreAmount, AirSystem.DefaultDrainRate,
                "one burst must not out-earn a second of drain");
            Assert.Less(AirSystem.CapsuleRestoreAmount, 3f * AirSystem.DefaultDrainRate,
                "a capsule is a find, not a refill");
            Assert.LessOrEqual(AirSystem.DrillRestoreAmount, AirSystem.BurstRestoreAmount,
                "drill restore is the survival floor, not a shortcut — it must never out-earn any other source");
        }

        // ── v3.1 arcade pivot: drill restore (§8, §6.1) ───────────────────────

        [Test]
        public void RestoreDrill_AddsCorrectAmount()
        {
            var air = new AirSystem();
            air.Tick(10f); // drain to 50 %
            Assert.AreEqual(50f, air.Air, Delta, "precondition");

            air.RestoreDrill();

            Assert.AreEqual(50.5f, air.Air, Delta);
        }

        [Test]
        public void RestoreDrill_ClampsAtMax()
        {
            var air = new AirSystem();
            air.Tick(0.04f); // drain 0.2 % → Air = 99.8
            Assert.AreEqual(99.8f, air.Air, Delta, "precondition");

            air.RestoreDrill(); // +0.5 % would overfill to 100.3

            Assert.AreEqual(AirSystem.MaxAir, air.Air, Delta);
        }

        [Test]
        public void RestoreDrill_TenTimesFromNinetyFive_ClampsAtMax_NoHigher()
        {
            var air = new AirSystem();
            air.Tick(1f); // drain 5 % → Air = 95
            Assert.AreEqual(95f, air.Air, Delta, "precondition");

            for (int i = 0; i < 10; i++)
                air.RestoreDrill();

            Assert.AreEqual(AirSystem.MaxAir, air.Air, Delta, "must clamp at 100, never overshoot");
        }

        [Test]
        public void RestoreBombChain_ClampsAtMax()
        {
            var air = new AirSystem();
            air.Tick(1f);              // drain 5 %
            air.RestoreBombChain(20);  // +60 % would massively overfill

            Assert.AreEqual(AirSystem.MaxAir, air.Air, Delta);
        }

        [Test]
        public void RestoreBombChain_NonPositiveCount_RestoresNothing()
        {
            var air = new AirSystem();
            air.Tick(10f);
            float before = air.Air;

            air.RestoreBombChain(0);
            air.RestoreBombChain(-5);

            Assert.AreEqual(before, air.Air, Delta, "a bad caller must never move the air bar");
        }

        [Test]
        public void RestoreBurst_AndBombChain_FireAirChanged()
        {
            var air = new AirSystem();
            air.Tick(10f);
            int changes = 0;
            air.AirChanged += _ => changes++;

            air.RestoreBurst();
            air.RestoreBombChain(2);

            Assert.AreEqual(2, changes, "each restore that moves the bar reports once");
        }

        [Test]
        public void Reset_RestoresFullAirAndClearsDepleted()
        {
            var air = new AirSystem();
            for (int i = 0; i < 1000; i++)
                air.Tick(1f);
            Assert.IsTrue(air.IsEmpty);

            air.Reset();

            Assert.AreEqual(AirSystem.MaxAir, air.Air, Delta);
            Assert.IsFalse(air.IsEmpty);

            // AirDepleted should be able to fire again after reset
            int count = 0;
            air.AirDepleted += () => count++;
            for (int i = 0; i < 1000; i++)
                air.Tick(1f);
            Assert.AreEqual(1, count, "AirDepleted fires again after Reset()");
        }
    }

    [TestFixture]
    public class HealthSystemTests
    {
        [Test]
        public void StartsAtMaxHearts()
        {
            var health = new HealthSystem();
            Assert.AreEqual(HealthSystem.MaxHearts, health.Hearts);
            Assert.IsTrue(health.IsAlive);
            Assert.IsFalse(health.IsInvincible);
        }

        [Test]
        public void TryTakeDamage_ReducesHearts()
        {
            var health = new HealthSystem();
            bool hit = health.TryTakeDamage();
            Assert.IsTrue(hit);
            Assert.AreEqual(HealthSystem.MaxHearts - 1, health.Hearts);
        }

        [Test]
        public void TryTakeDamage_StartsIFrames()
        {
            var health = new HealthSystem();
            health.TryTakeDamage();
            Assert.IsTrue(health.IsInvincible);
        }

        [Test]
        public void TryTakeDamage_DuringIFrames_ReturnsFalse()
        {
            var health = new HealthSystem();
            health.TryTakeDamage();           // first hit — i-frames start
            bool secondHit = health.TryTakeDamage(); // should be blocked
            Assert.IsFalse(secondHit);
            Assert.AreEqual(HealthSystem.MaxHearts - 1, health.Hearts, "second hit must not land");
        }

        [Test]
        public void Tick_ExpiresIFrames()
        {
            var health = new HealthSystem();
            health.TryTakeDamage();
            Assert.IsTrue(health.IsInvincible);

            health.Tick(HealthSystem.IFrameDuration + 0.01f);

            Assert.IsFalse(health.IsInvincible);
        }

        [Test]
        public void TryTakeDamage_AcceptedAfterIFramesExpire()
        {
            var health = new HealthSystem();
            health.TryTakeDamage();
            health.Tick(HealthSystem.IFrameDuration + 0.01f);

            bool secondHit = health.TryTakeDamage();
            Assert.IsTrue(secondHit);
            Assert.AreEqual(HealthSystem.MaxHearts - 2, health.Hearts);
        }

        [Test]
        public void HealthDepleted_FiresAtZeroHearts()
        {
            var health = new HealthSystem();
            bool depleted = false;
            health.HealthDepleted += () => depleted = true;

            for (int i = 0; i < HealthSystem.MaxHearts; i++)
            {
                health.TryTakeDamage();
                health.Tick(HealthSystem.IFrameDuration + 0.01f); // expire i-frames between hits
            }

            Assert.IsTrue(depleted);
            Assert.IsFalse(health.IsAlive);
        }

        [Test]
        public void HealthDepleted_FiresExactlyOnce()
        {
            var health = new HealthSystem();
            int count = 0;
            health.HealthDepleted += () => count++;

            // Kill the avatar
            for (int i = 0; i < HealthSystem.MaxHearts; i++)
            {
                health.TryTakeDamage();
                health.Tick(HealthSystem.IFrameDuration + 0.01f);
            }

            // Further attempts should be ignored
            health.TryTakeDamage();
            health.TryTakeDamage();

            Assert.AreEqual(1, count, "HealthDepleted must fire exactly once per run");
        }

        [Test]
        public void TryTakeDamage_WhenDead_ReturnsFalse()
        {
            var health = new HealthSystem();
            for (int i = 0; i < HealthSystem.MaxHearts; i++)
            {
                health.TryTakeDamage();
                health.Tick(HealthSystem.IFrameDuration + 0.01f);
            }

            bool extra = health.TryTakeDamage();
            Assert.IsFalse(extra);
            Assert.AreEqual(0, health.Hearts);
        }

        [Test]
        public void HeartsChanged_FiresWithNewCount()
        {
            var health = new HealthSystem();
            int? reported = null;
            health.HeartsChanged += h => reported = h;

            health.TryTakeDamage();

            Assert.IsNotNull(reported);
            Assert.AreEqual(HealthSystem.MaxHearts - 1, reported.Value);
        }

        [Test]
        public void Reset_RestoresFullHealthAndClearsState()
        {
            var health = new HealthSystem();
            health.TryTakeDamage();
            health.Reset();

            Assert.AreEqual(HealthSystem.MaxHearts, health.Hearts);
            Assert.IsTrue(health.IsAlive);
            Assert.IsFalse(health.IsInvincible);
        }

        [Test]
        public void Reset_AllowsHealthDepletedToFireAgain()
        {
            var health = new HealthSystem();

            // First run: deplete
            for (int i = 0; i < HealthSystem.MaxHearts; i++)
            {
                health.TryTakeDamage();
                health.Tick(HealthSystem.IFrameDuration + 0.01f);
            }

            health.Reset();

            // Second run: deplete again
            int count = 0;
            health.HealthDepleted += () => count++;
            for (int i = 0; i < HealthSystem.MaxHearts; i++)
            {
                health.TryTakeDamage();
                health.Tick(HealthSystem.IFrameDuration + 0.01f);
            }

            Assert.AreEqual(1, count, "HealthDepleted fires again after Reset()");
        }
    }

    [TestFixture]
    public class BlockTypesTests
    {
        // ── CellType extensions ──────────────────────────────────────

        [Test]
        public void Hard_IsSolid_NotFusable_IsDrillable()
        {
            Assert.IsTrue(CellType.Hard.IsSolid());
            Assert.IsFalse(CellType.Hard.CanFuse());
            Assert.IsTrue(CellType.Hard.IsDrillable());
        }

        [Test]
        public void Steel_IsSolid_NotDrillable()
        {
            Assert.IsTrue(CellType.Steel.IsSolid());
            Assert.IsFalse(CellType.Steel.CanFuse());
            Assert.IsFalse(CellType.Steel.IsDrillable());
        }

        [Test]
        public void AirCapsule_IsSolid_IsDrillable_NotFusable()
        {
            Assert.IsTrue(CellType.AirCapsule.IsSolid());
            Assert.IsFalse(CellType.AirCapsule.CanFuse());
            Assert.IsTrue(CellType.AirCapsule.IsDrillable());
        }

        [Test]
        public void Diamond_IsSolid_IsDrillable_NotFusable()
        {
            Assert.IsTrue(CellType.Diamond.IsSolid());
            Assert.IsFalse(CellType.Diamond.CanFuse());
            Assert.IsTrue(CellType.Diamond.IsDrillable());
        }

        [Test]
        public void DrillResult_Hard_IsHardCracked()
        {
            Assert.AreEqual(CellType.HardCracked, CellType.Hard.DrillResult());
        }

        [Test]
        public void DrillResult_HardCracked_IsEmpty()
        {
            Assert.AreEqual(CellType.Empty, CellType.HardCracked.DrillResult());
        }

        [Test]
        public void DrillResult_AirCapsule_IsEmpty()
        {
            Assert.AreEqual(CellType.Empty, CellType.AirCapsule.DrillResult());
        }

        [Test]
        public void DrillResult_Diamond_IsEmpty()
        {
            Assert.AreEqual(CellType.Empty, CellType.Diamond.DrillResult());
        }

        // ── GridModel.Drill() ────────────────────────────────────────

        [Test]
        public void Drill_Hard_FirstHit_LeavesHardCracked()
        {
            var grid = GridModel.FromStringMap(new[] { "H." });
            Assert.IsTrue(grid.Drill(new GridPos(0, 0)));
            Assert.AreEqual(CellType.HardCracked, grid.Get(new GridPos(0, 0)));
        }

        [Test]
        public void Drill_Hard_SecondHit_BecomesEmpty()
        {
            var grid = GridModel.FromStringMap(new[] { "H." });
            grid.Drill(new GridPos(0, 0)); // → HardCracked
            grid.Drill(new GridPos(0, 0)); // → Empty
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 0)));
        }

        [Test]
        public void Drill_Steel_ReturnsFalse_CellUnchanged()
        {
            var grid = GridModel.FromStringMap(new[] { "S." });
            Assert.IsFalse(grid.Drill(new GridPos(0, 0)));
            Assert.AreEqual(CellType.Steel, grid.Get(new GridPos(0, 0)));
        }

        [Test]
        public void Drill_AirCapsule_BecomesEmpty()
        {
            var grid = GridModel.FromStringMap(new[] { "P." });
            Assert.IsTrue(grid.Drill(new GridPos(0, 0)));
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 0)));
        }

        [Test]
        public void Drill_Diamond_BecomesEmpty()
        {
            var grid = GridModel.FromStringMap(new[] { "D." });
            Assert.IsTrue(grid.Drill(new GridPos(0, 0)));
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 0)));
        }

        // ── ChunkSystem with new types ───────────────────────────────

        [Test]
        public void Hard_DoesNotFuseWithAdjacentHard()
        {
            var grid = GridModel.FromStringMap(new[] { "HH" });
            var chunks = ChunkSystem.ComputeChunks(grid);
            Assert.AreEqual(2, chunks.Count, "Hard blocks must not fuse");
        }

        [Test]
        public void Steel_DoesNotFuseWithAdjacentSteel()
        {
            var grid = GridModel.FromStringMap(new[] { "SS" });
            var chunks = ChunkSystem.ComputeChunks(grid);
            Assert.AreEqual(2, chunks.Count, "Steel blocks must not fuse");
        }

        // ── CollapseSystem: Steel cleared on collapse ────────────────

        [Test]
        public void Collapse_SteelInShiftedRow_IsClearedToEmpty()
        {
            // Row 0: Steel at col 0
            // Row 1: void → triggers collapse, shifts row 0 down
            // Row 2: solid floor (so row 1 has something above it)
            var grid = GridModel.FromStringMap(new[]
            {
                "S.",
                "..",
                "AA"
            });
            var collapse = new CollapseSystem(grid);
            collapse.Resolve(new GridPos(9, 9)); // avatar far away

            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 1)),
                "Steel must be cleared during collapse shift, not copied down");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 0)),
                "top row must be filled with void after collapse");
        }

        [Test]
        public void Collapse_ColorBlockNextToSteel_ColorSurvives()
        {
            // col 0 = Steel, col 1 = ColorA — only Steel must be cleared
            var grid = GridModel.FromStringMap(new[]
            {
                "SA",
                "..",
                "AA"
            });
            var collapse = new CollapseSystem(grid);
            collapse.Resolve(new GridPos(9, 9));

            Assert.AreEqual(CellType.Empty,  grid.Get(new GridPos(0, 1)), "Steel cleared");
            Assert.AreEqual(CellType.ColorA, grid.Get(new GridPos(1, 1)), "ColorA survives");
        }

        // ── CollapseSystem: PerfectClear event (v3 rename of RowCollapsed) ──

        [Test]
        public void Collapse_VoidRow_FiresPerfectClear_WithRowIndex()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                "AA",
                "..",
                "AA"
            });
            var collapse = new CollapseSystem(grid);
            var rows = new List<int>();
            collapse.PerfectClear += rows.Add;

            int collapsed = collapse.Resolve(new GridPos(9, 9));

            Assert.AreEqual(1, collapsed);
            CollectionAssert.AreEqual(new[] { 1 }, rows, "PerfectClear carries the collapsed row index");
        }

        [Test]
        public void Collapse_EmptySpawnRowsAboveNothing_DoNotFirePerfectClear()
        {
            // Void rows with no solid above them are sky, not a clear.
            var grid = GridModel.FromStringMap(new[]
            {
                "..",
                "..",
                "AA"
            });
            var collapse = new CollapseSystem(grid);
            bool fired = false;
            collapse.PerfectClear += _ => fired = true;

            collapse.Resolve(new GridPos(9, 9));

            Assert.IsFalse(fired, "empty spawn zone must never count as a Perfect Clear");
        }

        // ── AvatarModel: Drilled event carries old type ───────────────

        [Test]
        public void AvatarModel_Drilled_ReportsOldCellType_Hard()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                ".",
                "H",
                "A"
            });
            var avatar = new AvatarModel(grid, new GridPos(0, 0));
            CellType? reported = null;
            avatar.Drilled += (_, old, __) => reported = old;

            avatar.TryDrill(0, 1); // drill the Hard block below

            Assert.AreEqual(CellType.Hard, reported);
            Assert.AreEqual(CellType.HardCracked, grid.Get(new GridPos(0, 1)));
        }

        [Test]
        public void AvatarModel_Drilled_ReportsOldCellType_AirCapsule()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                ".",
                "P",
                "A"
            });
            var avatar = new AvatarModel(grid, new GridPos(0, 0));
            CellType? reported = null;
            avatar.Drilled += (_, old, __) => reported = old;

            avatar.TryDrill(0, 1);

            Assert.AreEqual(CellType.AirCapsule, reported);
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 1)));
        }

        [Test]
        public void AvatarModel_DrillSteel_ReturnsFalse_NoDrilledEvent()
        {
            var grid = GridModel.FromStringMap(new[]
            {
                ".",
                "S",
                "A"
            });
            var avatar = new AvatarModel(grid, new GridPos(0, 0));
            bool fired = false;
            avatar.Drilled += (_, __, ___) => fired = true;

            bool result = avatar.TryDrill(0, 1);

            Assert.IsFalse(result, "TryDrill returns false on Steel");
            Assert.IsFalse(fired, "Drilled event must not fire when drill fails");
        }
    }

    [TestFixture]
    public class BombSystemTests
    {
        private static (GridModel grid, CollapseSystem collapse, BombSystem bombs) Make(string[] rows)
        {
            var grid     = GridModel.FromStringMap(rows);
            var collapse = new CollapseSystem(grid);
            var bombs    = new BombSystem(grid, collapse);
            return (grid, collapse, bombs);
        }

        // ── CellType ────────────────────────────────────────────────────

        [Test]
        public void Bomb_IsSolid_NotDrillable_NotFusable()
        {
            Assert.IsTrue(CellType.Bomb.IsSolid());
            Assert.IsFalse(CellType.Bomb.IsDrillable());
            Assert.IsFalse(CellType.Bomb.CanFuse());
        }

        [Test]
        public void GridModel_Drill_Bomb_ReturnsFalse()
        {
            var grid = GridModel.FromStringMap(new[] { "X." });
            Assert.IsFalse(grid.Drill(new GridPos(0, 0)), "Bomb must not be drillable");
            Assert.AreEqual(CellType.Bomb, grid.Get(new GridPos(0, 0)));
        }

        // ── Arming by drill ──────────────────────────────────────────────

        [Test]
        public void NotifyDrilled_Adjacent_ArmsBomb()
        {
            // Bomb at (1,0), drill happens at (0,0) — adjacent left.
            var (_, __, bombs) = Make(new[] { "AXA" });
            bool armed = false;
            bombs.BombArmed += _ => armed = true;

            bombs.NotifyDrilled(new GridPos(0, 0));

            Assert.IsTrue(armed, "bomb adjacent to drilled cell must be armed");
        }

        [Test]
        public void NotifyDrilled_NotAdjacent_DoesNotArm()
        {
            // Bomb at col 4, drill at col 0 — not adjacent.
            var (_, __, bombs) = Make(new[] { "A...X" });
            bool armed = false;
            bombs.BombArmed += _ => armed = true;

            bombs.NotifyDrilled(new GridPos(0, 0));

            Assert.IsFalse(armed);
        }

        [Test]
        public void NotifyDrilled_AlreadyArmed_DoesNotRearm()
        {
            // Arm once, then try to arm again — fuse should not reset.
            var (grid, collapse, bombs) = Make(new[] { "AX", ".A" });
            int armCount = 0;
            bombs.BombArmed += _ => armCount++;

            bombs.NotifyDrilled(new GridPos(0, 0)); // arms bomb at (1,0)
            bombs.NotifyDrilled(new GridPos(0, 0)); // already armed — must not fire again

            Assert.AreEqual(1, armCount, "BombArmed must fire exactly once per arm");
        }

        // ── Fuse expiry & explosion ──────────────────────────────────────

        [Test]
        public void Tick_FuseExpires_BombExplodedFired_CellCleared()
        {
            var (grid, _, bombs) = Make(new[] { "AX", ".A" });
            bombs.NotifyDrilled(new GridPos(0, 0)); // arm bomb at (1,0)

            bool exploded = false;
            bombs.BombExploded += _ => exploded = true;

            GridPos farAway = new GridPos(9, 9);
            bombs.Tick(BombSystem.FuseDuration + 0.01f, farAway);

            Assert.IsTrue(exploded);
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 0)), "bomb cell cleared on detonation");
        }

        // ── Blast effects ─────────────────────────────────────────────────

        [Test]
        public void Blast_Color_ClearedToEmpty()
        {
            // Map: "AXA" at row 1 → Bomb at (1,1). ColorA at (1,0) and (1,2) — radius 1 up/down.
            // Arm via (0,1): its right neighbor is (1,1) = Bomb.
            var (grid, _, bombs) = Make(new[]
            {
                "AAA",  // row 0 — (1,0): distance 1 above bomb
                "AXA",  // row 1 — bomb at (1,1)
                "AAA",  // row 2 — (1,2): distance 1 below bomb
            });
            bombs.NotifyDrilled(new GridPos(0, 1));
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 0)), "color above cleared");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 2)), "color below cleared");
        }

        [Test]
        public void Blast_Hard_ClearedToEmpty()
        {
            // Bomb at (1,0), Hard at (0,0) — left neighbor, radius 1.
            var (grid, _, bombs) = Make(new[] { "HX", "AA" });
            bombs.NotifyDrilled(new GridPos(1, 1)); // arm via below
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 0)), "Hard destroyed by blast");
        }

        [Test]
        public void Blast_Steel_SoftenedToHard()
        {
            // Bomb at (1,0), Steel at (0,0) — left neighbor, radius 1.
            var (grid, _, bombs) = Make(new[] { "SX", "AA" });
            bombs.NotifyDrilled(new GridPos(1, 1)); // arm via below
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(CellType.Hard, grid.Get(new GridPos(0, 0)), "Steel softened to Hard by blast");
        }

        [Test]
        public void Blast_AirCapsule_ClearedToEmpty_AndLiberated()
        {
            // Bomb at (1,0), AirCapsule at (0,0) — left, radius 1.
            var (grid, _, bombs) = Make(new[] { "PX", "AA" });
            GridPos? liberated = null;
            bombs.AirCapsuleLiberated += p => liberated = p;

            bombs.NotifyDrilled(new GridPos(1, 1)); // arm via below
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 0)), "capsule cell cleared");
            Assert.AreEqual(new GridPos(0, 0), liberated,
                "v3: the blast frees the capsule instead of wasting it (design rule 3)");
        }

        [Test]
        public void Blast_NoAirCapsule_DoesNotFireLiberated()
        {
            var (_, __, bombs) = Make(new[] { "AX", "AA" });
            bool liberated = false;
            bombs.AirCapsuleLiberated += _ => liberated = true;

            bombs.NotifyDrilled(new GridPos(1, 1));
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.IsFalse(liberated);
        }

        [Test]
        public void Blast_Diamond_ClearedToEmpty_AndLiberated()
        {
            // Bomb at (1,0), Diamond at (0,0) — left, radius 1.
            var (grid, _, bombs) = Make(new[] { "DX", "AA" });
            GridPos? liberated = null;
            bombs.DiamondLiberated += p => liberated = p;

            bombs.NotifyDrilled(new GridPos(1, 1)); // arm via below
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 0)), "diamond cell cleared");
            Assert.AreEqual(new GridPos(0, 0), liberated,
                "R4: the blast frees the diamond instead of wasting it, same as an air capsule");
        }

        [Test]
        public void Blast_NoDiamond_DoesNotFireDiamondLiberated()
        {
            var (_, __, bombs) = Make(new[] { "AX", "AA" });
            bool liberated = false;
            bombs.DiamondLiberated += _ => liberated = true;

            bombs.NotifyDrilled(new GridPos(1, 1));
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.IsFalse(liberated);
        }

        // ── BombScored: payload × chain multiplier ────────────────────────

        [Test]
        public void BombScored_LoneBomb_ReportsFiveBlocksAtChainOne()
        {
            // Bomb at (1,1). Cross blast reaches: (1,0) up, (1,2)+(1,3) down, (0,1) left, (2,1) right = 5.
            // Armed via ArmBombAt (non-player, ChainBlastRadius=2) so the classic 2-deep reach still
            // applies here — this test is about the BombScored formula, not the arming source
            // (§5.3's DirectBlastRadius vs ChainBlastRadius split has its own dedicated tests below).
            var (_, __, bombs) = Make(new[]
            {
                "AAA",
                "AXA",
                "AAA",
                "AAA",
            });
            var scored = new List<(int destroyed, int chain)>();
            bombs.BombScored += (d, c) => scored.Add((d, c));

            bombs.ArmBombAt(new GridPos(1, 1));
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(1, scored.Count, "one detonation, one score event");
            Assert.AreEqual(5, scored[0].destroyed);
            Assert.AreEqual(1, scored[0].chain, "a lone bomb is chain 1");
        }

        [Test]
        public void BombScored_SteelSoftened_DoesNotCountAsDestroyed()
        {
            // Bomb at (1,0): left hits Steel (softened, not destroyed), below hits ColorA.
            var (_, __, bombs) = Make(new[] { "SX", "AA" });
            var scored = new List<(int destroyed, int chain)>();
            bombs.BombScored += (d, c) => scored.Add((d, c));

            bombs.NotifyDrilled(new GridPos(1, 1));
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(1, scored[0].destroyed, "only the ColorA below counts; Steel merely softened");
        }

        [Test]
        public void BombScored_TwoBombChain_FiresTwice_WithRisingMultiplier()
        {
            var (_, __, bombs) = Make(new[] { "AXX", "AAA" });
            var chains = new List<int>();
            bombs.BombScored += (_, c) => chains.Add(c);

            bombs.NotifyDrilled(new GridPos(0, 0)); // arm bomb at (1,0)
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            CollectionAssert.AreEqual(new[] { 1, 2 }, chains,
                "the starter scores at ×1, the sympathetic detonation at ×2");
        }

        [Test]
        public void BombScored_ThreeBombChain_MultiplierReachesThree()
        {
            var (_, __, bombs) = Make(new[] { "AXXX", "AAAA" });
            var chains = new List<int>();
            bombs.BombScored += (_, c) => chains.Add(c);

            bombs.NotifyDrilled(new GridPos(0, 0)); // arm bomb at (1,0)
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, chains);
        }

        [Test]
        public void Blast_RadiusEdge_Cleared_BeyondRadius_Unaffected()
        {
            // Bomb at (1,2). Armed via ArmBombAt (non-player) → ChainBlastRadius = 2.
            // (1,0): 2 above → IN radius → cleared.
            // (1,5): 3 below → OUT of radius → untouched.
            var (grid, _, bombs) = Make(new[]
            {
                "AAA",  // row 0 — (1,0): distance 2 above bomb
                "AAA",  // row 1 — distance 1 above
                "AXA",  // row 2 — bomb at (1,2)
                "AAA",  // row 3 — distance 1 below
                "AAA",  // row 4 — distance 2 below
                "AAA",  // row 5 — (1,5): distance 3 → outside radius
            });
            bombs.ArmBombAt(new GridPos(1, 2));
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(CellType.Empty,  grid.Get(new GridPos(1, 0)), "radius-edge cell cleared");
            Assert.AreEqual(CellType.ColorA, grid.Get(new GridPos(1, 5)), "cell beyond radius unaffected");
        }

        // ── v3.2: DirectBlastRadius (player) vs ChainBlastRadius (everything else) — §5.3 ────

        [Test]
        public void Blast_ArmedByPlayerDrill_DistanceOne_Destroyed()
        {
            // Bomb at (1,2), armed the way the player does (NotifyDrilled) → DirectBlastRadius = 1.
            // (1,1): distance 1 above → IN radius → cleared.
            var (grid, _, bombs) = Make(new[]
            {
                "AAA",  // row 0
                "AAA",  // row 1 — (1,1): distance 1 above bomb
                "AXA",  // row 2 — bomb at (1,2)
                "AAA",  // row 3
            });
            bombs.NotifyDrilled(new GridPos(0, 2)); // arm via left neighbor — player drill
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 1)),
                "distance-1 cell must be destroyed even at DirectBlastRadius");
        }

        [Test]
        public void Blast_ArmedByPlayerDrill_DistanceTwo_Survives()
        {
            // Bomb at (1,2), armed via player drill → DirectBlastRadius = 1.
            // (1,0): distance 2 above → OUTSIDE DirectBlastRadius → must survive.
            var (grid, _, bombs) = Make(new[]
            {
                "AAA",  // row 0 — (1,0): distance 2 above bomb
                "AAA",  // row 1 — distance 1 above
                "AXA",  // row 2 — bomb at (1,2)
                "AAA",  // row 3
            });
            bombs.NotifyDrilled(new GridPos(0, 2)); // arm via left neighbor — player drill
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(CellType.ColorA, grid.Get(new GridPos(1, 0)),
                "distance-2 cell must survive a player-armed bomb's DirectBlastRadius");
        }

        [Test]
        public void Blast_ArmedByBurstShockwave_DistanceTwo_Destroyed()
        {
            // Bomb at (1,2), armed via ArmBombAt — GravitySystem.BombArmedByBurst's wiring — so it
            // keeps ChainBlastRadius = 2, reaching a cell two rows above.
            var (grid, _, bombs) = Make(new[]
            {
                "AAA",  // row 0 — (1,0): distance 2 above bomb
                "AAA",  // row 1
                "AXA",  // row 2 — bomb at (1,2)
                "AAA",  // row 3
            });
            bombs.ArmBombAt(new GridPos(1, 2));
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 0)),
                "a burst-armed bomb keeps the full ChainBlastRadius");
        }

        [Test]
        public void Blast_ArmedByChunkLanding_KeepsChainBlastRadius()
        {
            // A single-cell chunk landing next to the bomb arms it as non-player (§5.3 item 1).
            // Bomb at (1,2); the landed chunk's sole cell is (0,2) — its left neighbor.
            var (grid, _, bombs) = Make(new[]
            {
                "AAA",  // row 0 — (1,0): distance 2 above bomb
                "AAA",  // row 1
                "AXA",  // row 2 — bomb at (1,2)
                "AAA",  // row 3
            });
            var helperGrid = GridModel.FromStringMap(new[] { "A", "A", "A", "A" }); // cell at (0,2) among others
            Chunk chunk = ChunkSystem.ComputeChunks(helperGrid)[0]; // the whole fused column is one chunk, contains (0,2)

            bombs.NotifyChunkLanded(chunk);
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 0)),
                "a chunk-landing-armed bomb keeps the full ChainBlastRadius");
        }

        [Test]
        public void SympatheticChain_FirstBombPlayerRadiusOne_SecondBombChainRadiusTwo()
        {
            // Bomb A at (1,0) — armed by the player (NotifyDrilled) → DirectBlastRadius = 1.
            // Bomb B at (2,0) — distance 1 from A, ignited sympathetically → ChainBlastRadius = 2.
            // B's own blast should reach (4,0), two cells to its right (distance 2 from B).
            var (grid, _, bombs) = Make(new[] { "AXXAA" });
            bombs.NotifyDrilled(new GridPos(0, 0)); // arm bomb A via left neighbor — player drill
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 0)), "bomb A cleared");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(2, 0)), "bomb B cleared (sympathetic)");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(3, 0)),
                "B's ChainBlastRadius (distance 1) must destroy this cell");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(4, 0)),
                "B's ChainBlastRadius (distance 2) must destroy this cell even though B was " +
                "sympathetically triggered by a player-armed bomb");
        }

        [Test]
        public void SympatheticChain_FirstBombPlayerRadiusOne_ReportsCorrectBlocksDestroyed()
        {
            // Same layout as above — verify BombScored's blocksDestroyed per detonation stays
            // correct for each radius (item 4: scoring is unaffected by the radius split).
            var (_, __, bombs) = Make(new[] { "AXXAA" });
            var scored = new List<(int destroyed, int chain)>();
            bombs.BombScored += (d, c) => scored.Add((d, c));

            bombs.NotifyDrilled(new GridPos(0, 0)); // arm bomb A — player drill
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(2, scored.Count, "two detonations");
            // Bomb A (DirectBlastRadius=1): destroys (0,0) to its left and clears bomb B's cell
            // (hitting a bomb counts as a destroyed block, same as any other solid — the sympathetic
            // ignition itself is scored separately when bomb B detonates).
            Assert.AreEqual(2, scored[0].destroyed, "bomb A's radius-1 blast destroys (0,0) and bomb B's cell");
            Assert.AreEqual(1, scored[0].chain);
            // Bomb B (ChainBlastRadius=2): destroys (3,0) and (4,0).
            Assert.AreEqual(2, scored[1].destroyed, "bomb B's radius-2 blast destroys both cells to its right");
            Assert.AreEqual(2, scored[1].chain);
        }

        // ── R5.18 dodge validation: DirectBlastRadius keeps a player-armed bomb dodgeable ──────

        [Test]
        public void PlayerCanDodge_DirectBomb_WithOneStep()
        {
            // Bomb at (3,5). Player drills (3,4) — adjacent above — to arm it (DirectBlastRadius=1).
            // A single-cell side-step, either direction, is a clean dodge: (2,4) and (4,4) are
            // diagonal to the bomb — never on the cardinal cross at ANY radius. Two independent
            // BombSystem instances (one per side) so each detonation is tested in isolation.
            bool hitLeft = false, hitRight = false;

            var (gridLeft, __, bombsLeft) = Make(new[]
            {
                "AAAAAAA", "AAAAAAA", "AAAAAAA", "AAAAAAA", "AAAAAAA", "AAAXAAA", "AAAAAAA",
            });
            bombsLeft.NotifyDrilled(new GridPos(3, 4));
            bombsLeft.AvatarHitByBlast += _ => hitLeft = true;
            bombsLeft.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(2, 4));

            var (gridRight, ___, bombsRight) = Make(new[]
            {
                "AAAAAAA", "AAAAAAA", "AAAAAAA", "AAAAAAA", "AAAAAAA", "AAAXAAA", "AAAAAAA",
            });
            bombsRight.NotifyDrilled(new GridPos(3, 4));
            bombsRight.AvatarHitByBlast += _ => hitRight = true;
            bombsRight.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(4, 4));

            Assert.IsFalse(hitLeft, "stepping to (2,4) must dodge a DirectBlastRadius bomb");
            Assert.IsFalse(hitRight, "stepping to (4,4) must dodge a DirectBlastRadius bomb");
            Assert.AreEqual(CellType.ColorA, gridLeft.Get(new GridPos(2, 4)), "(2,4) is never on the cross — untouched");
            Assert.AreEqual(CellType.ColorA, gridRight.Get(new GridPos(4, 4)), "(4,4) is never on the cross — untouched");

            // The player's OLD spot — the cell they drilled to arm the bomb — is squarely in the
            // blast (up-axis, distance 1). This is why standing still, or drilling-and-staying, kills.
            Assert.AreEqual(CellType.Empty, gridLeft.Get(new GridPos(3, 4)),
                "the drilled cell (the old spot) is destroyed by the up-axis at distance 1");
        }

        [Test]
        public void PlayerCanDodge_DirectBomb_ByDrillingDown()
        {
            // Same bomb (3,5), armed the same way (drill (3,4) above it, DirectBlastRadius=1).
            //
            // ⚠️ Correction from spec: (3,6) — one row straight down from the bomb — is NOT a safe
            // spot. It sits on the down-axis at distance 1, which IS inside DirectBlastRadius (the
            // cross hits all four cardinal neighbors, not just the side the player approached from)
            // — it is destroyed exactly like (3,4), the player's old spot in the test above. It is
            // also not "diagonal": (3,6) shares the bomb's own column, so it's on-axis by definition.
            // Nor is it reachable in a single move from (3,4) — it's two rows away, not adjacent.
            //
            // What DOES work, and is what this test actually verifies: the player keeps drilling
            // straight down PAST the immediately-dangerous row and ends up at (3,7) — still on the
            // bomb's own column, but at distance 2, outside DirectBlastRadius. This is the payoff of
            // the radius split: a ChainBlastRadius=2 bomb (burst/chunk-armed) would still reach that
            // far, but a bomb the player armed themselves does not.
            var (grid, _, bombs) = Make(new[]
            {
                "AAAAAAA", // row 0
                "AAAAAAA", // row 1
                "AAAAAAA", // row 2
                "AAAAAAA", // row 3
                "AAAAAAA", // row 4 — drilled cell, arms the bomb
                "AAAXAAA", // row 5 — bomb at col 3
                "AAAAAAA", // row 6 — distance 1 below the bomb: IN the blast
                "AAAAAAA", // row 7 — distance 2 below the bomb: OUTSIDE DirectBlastRadius
            });
            bombs.NotifyDrilled(new GridPos(3, 4));

            bool hit = false;
            bombs.AvatarHitByBlast += _ => hit = true;
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(3, 7));

            Assert.IsFalse(hit, "the player survives by drilling two rows down instead of stepping sideways");
            Assert.AreEqual(CellType.ColorA, grid.Get(new GridPos(3, 7)), "distance-2-down cell must survive DirectBlastRadius");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(3, 6)), "distance-1-down cell is still destroyed — down is on the cross too");
        }

        [Test]
        public void SympatheticBomb_HasLargerBlast()
        {
            // Bomb A at (3,5), armed by the player (DirectBlastRadius=1).
            // Bomb B at (3,4) — directly above A, distance 1 — inside A's own radius-1 reach, so A's
            // blast ignites B sympathetically. B then detonates at ChainBlastRadius=2 (§5.3 item 3:
            // sympathetic detonations ALWAYS get the wider radius, regardless of what triggered them).
            var (grid, _, bombs) = Make(new[]
            {
                "AAAAAAA", // row 0
                "AAAAAAA", // row 1
                "AAAAAAA", // row 2 — distance 2 above B
                "AAAAAAA", // row 3 — distance 1 above B
                "AAAXAAA", // row 4 — bomb B
                "AAAXAAA", // row 5 — bomb A
                "AAAAAAA", // row 6
            });
            // Arm A directly (its left neighbor), NOT via B, so only A starts ArmedByPlayer=true.
            bombs.NotifyDrilled(new GridPos(2, 5));

            var chains = new List<int>();
            bombs.BombScored += (_, c) => chains.Add(c);

            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            CollectionAssert.AreEqual(new[] { 1, 2 }, chains, "A detonates first, B second (sympathetic)");

            // Distance 2 from B, on B's own axes — reachable ONLY because B got ChainBlastRadius.
            // A alone (DirectBlastRadius=1) could never reach these: they're distance 3 (up) and
            // distance 2 laterally from A, both beyond A's own radius.
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(3, 2)), "distance 2 above B must be destroyed by B's ChainBlastRadius");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 4)), "distance 2 left of B must be destroyed by B's ChainBlastRadius");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(5, 4)), "distance 2 right of B must be destroyed by B's ChainBlastRadius");
        }

        [Test]
        public void PlayerSafe_FromChainedBombs_BecauseAlreadyFar()
        {
            // Bomb A at (3,5), armed by the player (DirectBlastRadius=1). Bomb B at (4,5) — A's right
            // neighbor, distance 1 — ignited sympathetically, detonates at ChainBlastRadius=2.
            // Player is at (2,4): off both A's and B's cross entirely (different row AND different
            // column from each bomb), so no radius — however wide — ever reaches them.
            var (grid, _, bombs) = Make(new[]
            {
                "AAAAAAA", // row 0
                "AAAAAAA", // row 1
                "AAAAAAA", // row 2
                "AAAAAAA", // row 3 — distance 2 above B
                "AAAAAAA", // row 4 — player stands at (2,4) here
                "AAAXXAA", // row 5 — bomb A (col 3), bomb B (col 4)
                "AAAAAAA", // row 6
                "AAAAAAA", // row 7 — distance 2 below B
            });
            // Arm A via its own up-neighbor — not adjacent to B, so only A starts ArmedByPlayer=true.
            bombs.NotifyDrilled(new GridPos(3, 4));

            GridPos player = new GridPos(2, 4);
            bool hit = false;
            bombs.AvatarHitByBlast += _ => hit = true;

            bombs.Tick(BombSystem.FuseDuration + 0.01f, player);

            Assert.IsFalse(hit, "the player at (2,4) is never on bomb A's or bomb B's row/column, at any radius");
            // Grid-state corroboration: (2,4) itself is never touched by either blast.
            Assert.AreEqual(CellType.ColorA, grid.Get(player), "(2,4) must survive both detonations untouched");
        }

        [Test]
        public void Blast_AvatarInRadius_AvatarHitByBlastFired()
        {
            // Bomb at (1,0), avatar at (1,1) — directly below, radius 1.
            var (_, __, bombs) = Make(new[]
            {
                "AXA",  // row 0 — bomb at col 1
                "AAA",  // row 1 — avatar here
                "AAA",  // row 2 — floor
            });
            GridPos avatar = new GridPos(1, 1);
            bool hit = false;
            bombs.AvatarHitByBlast += _ => hit = true;

            bombs.NotifyDrilled(new GridPos(0, 0)); // arm bomb at (1,0) via left neighbor
            bombs.Tick(BombSystem.FuseDuration + 0.01f, avatar);

            Assert.IsTrue(hit, "AvatarHitByBlast must fire when avatar is in blast radius");
        }

        // ── Sympathetic detonation ────────────────────────────────────────

        [Test]
        public void Blast_AdjacentBomb_TriggersSympatheticDetonation()
        {
            // Two bombs side by side: X at col 1 (armed), X at col 2 (unarmed).
            // When bomb at col 1 explodes, blast at radius 1 hits col 2 bomb → sympathetic.
            var (grid, _, bombs) = Make(new[] { "AXX", "AAA" });
            int explosionCount = 0;
            bombs.BombExploded += _ => explosionCount++;

            bombs.NotifyDrilled(new GridPos(0, 0)); // arm bomb at (1,0)
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.AreEqual(2, explosionCount, "sympathetic bomb must also detonate");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 0)), "first bomb cleared");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(2, 0)), "sympathetic bomb cleared");
        }

        // ── Pocket bomb ───────────────────────────────────────────────────

        [Test]
        public void PocketBomb_ExplodesImmediately_NoFuse()
        {
            var (grid, _, bombs) = Make(new[] { "AAA", "AAA", "AAA" });
            bombs.GrantPocketBomb();
            Assert.IsTrue(bombs.HasPocketBomb);

            bool exploded = false;
            bombs.BombExploded += _ => exploded = true;

            // Use pocket bomb at center of 3×3 grid — no fuse, instant blast.
            bool used = bombs.TryUsePocketBomb(new GridPos(1, 1), new GridPos(9, 9));

            Assert.IsTrue(used);
            Assert.IsTrue(exploded, "pocket bomb must explode immediately");
            // Cells at radius 1 in all 4 directions from (1,1) should be cleared.
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 0)), "above cleared");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 2)), "below cleared");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 1)), "left cleared");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(2, 1)), "right cleared");
        }

        [Test]
        public void PocketBomb_AfterUse_HasPocketBombFalse()
        {
            var (_, __, bombs) = Make(new[] { "AAA" });
            bombs.GrantPocketBomb();
            bombs.TryUsePocketBomb(new GridPos(1, 0), new GridPos(9, 9));

            Assert.IsFalse(bombs.HasPocketBomb, "pocket bomb consumed after use");
        }

        [Test]
        public void PocketBomb_WithoutGrant_ReturnsFalse()
        {
            var (_, __, bombs) = Make(new[] { "AAA" });
            bool result = bombs.TryUsePocketBomb(new GridPos(1, 0), new GridPos(9, 9));
            Assert.IsFalse(result);
        }

        // ── Collapse shift ────────────────────────────────────────────────

        [Test]
        public void ArmedBomb_SurvivesCollapse_PositionUpdated()
        {
            // Armed bomb at (0,0). Void row at (1) causes collapse — bomb shifts to (0,1).
            // After collapse, the bomb should still detonate and clear its new position.
            var (grid, collapse, bombs) = Make(new[]
            {
                "X.",  // row 0 — bomb + empty
                "..",  // row 1 — void → triggers collapse
                "AA",  // row 2 — solid floor to trigger HasSolidAbove
            });
            bombs.NotifyDrilled(new GridPos(1, 0)); // arm bomb at (0,0) via right neighbor

            // Collapse shifts row 0 down to row 1; armed bomb must follow.
            collapse.Resolve(new GridPos(9, 9));

            // Bomb should now be at (0,1) in the grid.
            Assert.AreEqual(CellType.Bomb, grid.Get(new GridPos(0, 1)), "bomb cell shifted down by collapse");

            // Tick past fuse — explosion must happen at new position.
            bool exploded = false;
            bombs.BombExploded += _ => exploded = true;
            bombs.Tick(BombSystem.FuseDuration + 0.01f, new GridPos(9, 9));

            Assert.IsTrue(exploded, "armed bomb must still explode after collapse shift");
            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(0, 1)), "bomb cleared at new position");
        }

        // ── Chunk landing arming ──────────────────────────────────────────

        [Test]
        public void NotifyChunkLanded_Adjacent_ArmsBomb()
        {
            // Grid: ColorA at (0,0), Bomb at (1,0) — right neighbor.
            // A single-cell chunk at (0,0) landing next to the bomb must arm it.
            var (_, __, bombs) = Make(new[] { "AX" });
            bool armed = false;
            bombs.BombArmed += _ => armed = true;

            // Build a chunk whose sole cell is (0,0) — position is what matters to BombSystem.
            var helperGrid = GridModel.FromStringMap(new[] { "A." });
            Chunk chunk = ChunkSystem.ComputeChunks(helperGrid)[0]; // cell (0,0), Color=ColorA
            bombs.NotifyChunkLanded(chunk); // ArmAdjacent(0,0) → right neighbor (1,0) = Bomb in _grid

            Assert.IsTrue(armed);
        }
    }

    [TestFixture]
    public class StrateGeneratorTests
    {
        // ── StrateDescriptor ─────────────────────────────────────────────────

        [Test]
        public void StrateDescriptor_MaxColors_ClampedTo1_3()
        {
            var lo = new StrateDescriptor(rows: 1, maxColors: 0);
            var hi = new StrateDescriptor(rows: 1, maxColors: 99);

            Assert.AreEqual(1, lo.MaxColors, "MaxColors clamped to minimum 1");
            Assert.AreEqual(3, hi.MaxColors, "MaxColors clamped to maximum 3");
        }

        // ── GenerateRow ───────────────────────────────────────────────────────

        [Test]
        public void GenerateRow_Width_IsExact()
        {
            var desc = new StrateDescriptor(rows: 1);
            var rng  = new System.Random(0);

            string row = StrateGenerator.GenerateRow(desc, StrateGenerator.DefaultWidth, rng);

            Assert.AreEqual(StrateGenerator.DefaultWidth, row.Length);
        }

        [Test]
        public void GenerateRow_ColorOnly_ContainsOnlyColorChars()
        {
            // No special rates → all cells should be A, B, or C.
            var desc = new StrateDescriptor(rows: 1, maxColors: 3);
            var rng  = new System.Random(42);

            for (int trial = 0; trial < 20; trial++)
            {
                string row = StrateGenerator.GenerateRow(desc, StrateGenerator.DefaultWidth, rng);
                foreach (char c in row)
                    Assert.IsTrue(c == 'A' || c == 'B' || c == 'C',
                        $"Unexpected char '{c}' in color-only strate");
            }
        }

        [Test]
        public void GenerateRow_HardRate100_AllHardCells()
        {
            // hardRate=1.0 means every cell rolls into the Hard bucket (after 0 empty/steel/bomb).
            var desc = new StrateDescriptor(rows: 1, hardRate: 1.0f);
            var rng  = new System.Random(0);
            string row = StrateGenerator.GenerateRow(desc, 6, rng);

            foreach (char c in row)
                Assert.AreEqual('H', c, "All cells should be Hard when hardRate=1");
        }

        [Test]
        public void GenerateRow_SteelRate100_AllSteelCells()
        {
            // steelRate wins before hard/air/color in priority order.
            var desc = new StrateDescriptor(rows: 1, steelRate: 1.0f);
            var rng  = new System.Random(0);
            string row = StrateGenerator.GenerateRow(desc, 6, rng);

            foreach (char c in row)
                Assert.AreEqual('S', c, "All cells should be Steel when steelRate=1");
        }

        [Test]
        public void GenerateRow_MaxColors1_OnlyColorA()
        {
            var desc = new StrateDescriptor(rows: 1, maxColors: 1);
            var rng  = new System.Random(7);

            for (int trial = 0; trial < 20; trial++)
            {
                string row = StrateGenerator.GenerateRow(desc, StrateGenerator.DefaultWidth, rng);
                foreach (char c in row)
                    Assert.AreEqual('A', c, "Only ColorA allowed when MaxColors=1");
            }
        }

        [Test]
        public void GenerateRow_EmptyRate100_ForcesOneColorBlock()
        {
            // Every cell wants to be empty, but the min-solid guard must inject one color.
            var desc = new StrateDescriptor(rows: 1, emptyRate: 1.0f, maxColors: 3);
            var rng  = new System.Random(0);

            for (int trial = 0; trial < 10; trial++)
            {
                string row = StrateGenerator.GenerateRow(desc, StrateGenerator.DefaultWidth, rng);
                int solidCount = 0;
                foreach (char c in row)
                    if (c != '.') solidCount++;

                Assert.AreEqual(1, solidCount, "Exactly one forced color block when emptyRate=1");
            }
        }

        [Test]
        public void GenerateRow_NoBombsAdjacentHorizontally()
        {
            // High bombRate triggers the adjacency guard frequently.
            var desc = new StrateDescriptor(rows: 1, bombRate: 0.9f, maxColors: 3);
            var rng  = new System.Random(1337);

            for (int trial = 0; trial < 50; trial++)
            {
                string row = StrateGenerator.GenerateRow(desc, StrateGenerator.DefaultWidth, rng);
                for (int i = 1; i < row.Length; i++)
                    Assert.IsFalse(row[i] == 'X' && row[i - 1] == 'X',
                        $"Adjacent bombs found at [{i-1}] and [{i}] in row: {row}");
            }
        }

        // ── Color cohesion / veins (§15.3) ────────────────────────────────────

        [Test]
        public void GenerateRow_InheritsColorFromTheRowAbove()
        {
            // A solid ColorB row above must pull most of the next row's colors to B.
            var desc = new StrateDescriptor(rows: 1, maxColors: 3);
            var rng  = new System.Random(4);
            string above = new string('B', 40);

            int inherited = 0, colored = 0;
            for (int trial = 0; trial < 30; trial++)
            {
                string row = StrateGenerator.GenerateRow(desc, 40, rng, above);
                foreach (char c in row)
                {
                    if (c != 'A' && c != 'B' && c != 'C') continue;
                    colored++;
                    if (c == 'B') inherited++;
                }
            }

            // Pure chance would be ~33%. Vertical cohesion is 0.75, so expect well over half.
            Assert.Greater(inherited / (double)colored, 0.6,
                "colors must follow the vein above, not re-roll independently");
        }

        [Test]
        public void CampaignBoard_ProducesVerticalVeins_LongEnoughForStreaks()
        {
            // §15.3: a straight-down driller should reach ×4+. Scored the way StreakTracker
            // actually works — only a DIFFERENT color breaks the run. Hard/HardCracked/AirCapsule
            // are streak-neutral by design, Empty is fallen past, and Steel/Bomb force a sidestep
            // that horizontal cohesion usually lands on the same color. So every non-color cell
            // is transparent here, which makes this a pure measure of column color coherence.
            for (int level = 1; level <= 10; level++)
            {
                string[] board = StrateGenerator.CampaignBoard(level);
                int best = 0;

                for (int x = 0; x < board[0].Length; x++)
                {
                    int streak = 0;
                    char color = '\0';

                    foreach (string row in board)
                    {
                        char c = row[x];
                        if (c != 'A' && c != 'B' && c != 'C') continue; // transparent

                        streak = (c == color) ? streak + 1 : 1;
                        color  = c;
                        if (streak > best) best = streak;
                    }
                }

                Assert.GreaterOrEqual(best, 4,
                    $"Level {level} must offer a ×4+ streak to a straight-down driller");
            }
        }

        [Test]
        public void GenerateRow_CohesionNeverViolatesMaxColors()
        {
            // A 2-color strate must not inherit a 'C' from a 3-color strate above it.
            var desc = new StrateDescriptor(rows: 1, maxColors: 2);
            var rng  = new System.Random(11);
            string above = new string('C', 30);

            for (int trial = 0; trial < 30; trial++)
            {
                string row = StrateGenerator.GenerateRow(desc, 30, rng, above);
                foreach (char c in row)
                    Assert.AreNotEqual('C', c, "inheritance must respect the strate's MaxColors");
            }
        }

        // ── Build ─────────────────────────────────────────────────────────────

        [Test]
        public void Build_SpawnZone_IsAllEmpty()
        {
            var strates = new[] { new StrateDescriptor(rows: 4) };
            var rng     = new System.Random(0);
            string[] board = StrateGenerator.Build(strates, StrateGenerator.DefaultWidth, rng);

            for (int r = 0; r < StrateGenerator.SpawnRows; r++)
                foreach (char c in board[r])
                    Assert.AreEqual('.', c, $"Spawn row {r} must be fully empty");
        }

        [Test]
        public void Build_FloorRow_IsAllColorA()
        {
            var strates = new[] { new StrateDescriptor(rows: 4) };
            var rng     = new System.Random(0);
            string[] board = StrateGenerator.Build(strates, StrateGenerator.DefaultWidth, rng);

            string floor = board[board.Length - 1];
            foreach (char c in floor)
                Assert.AreEqual('A', c, "Floor row must be solid ColorA");
        }

        [Test]
        public void Build_TotalRowCount_IsCorrect()
        {
            var strates = new[]
            {
                new StrateDescriptor(rows: 3),
                new StrateDescriptor(rows: 5),
            };
            var rng = new System.Random(0);
            string[] board = StrateGenerator.Build(strates, StrateGenerator.DefaultWidth, rng);

            int expected = StrateGenerator.SpawnRows + 3 + 5 + StrateGenerator.FloorRows;
            Assert.AreEqual(expected, board.Length);
        }

        [Test]
        public void Build_OutputParsesIntoGridModel()
        {
            var strates = new[]
            {
                new StrateDescriptor(rows: 4, hardRate: 0.1f, steelRate: 0.08f,
                                     bombRate: 0.06f, airRate: 0.05f, maxColors: 3),
            };
            var rng = new System.Random(999);
            string[] board = StrateGenerator.Build(strates, StrateGenerator.DefaultWidth, rng);

            // Must not throw — all characters must be valid GridModel map chars.
            Assert.DoesNotThrow(() => GridModel.FromStringMap(board));
        }

        // ── Campaign presets ──────────────────────────────────────────────────

        [Test]
        public void DefaultWidth_IsSevenForV3()
        {
            Assert.AreEqual(7, StrateGenerator.DefaultWidth);
        }

        [Test]
        public void CampaignBoard_UsesDefaultWidth_NotOldWidthOfNine()
        {
            for (int level = 1; level <= 10; level++)
            {
                string[] board = StrateGenerator.CampaignBoard(level);
                foreach (string row in board)
                    Assert.AreEqual(StrateGenerator.DefaultWidth, row.Length,
                        $"Level {level} row must match DefaultWidth (7), not the old width of 9");
            }
        }

        [Test]
        public void CampaignBoard_Level1_HasNoSpecialBlocks()
        {
            string[] board = StrateGenerator.CampaignBoard(1);

            foreach (string row in board)
                foreach (char c in row)
                    Assert.IsFalse(c == 'H' || c == 'S' || c == 'X' || c == 'P',
                        $"Level 1 must not contain special blocks, found '{c}'");
        }

        [Test]
        public void CampaignBoard_Level6_HasBombs()
        {
            string[] board = StrateGenerator.CampaignBoard(6);
            bool foundBomb = false;
            foreach (string row in board)
                foreach (char c in row)
                    if (c == 'X') { foundBomb = true; break; }

            Assert.IsTrue(foundBomb, "Level 6 introduces bombs (§5.8 ramp)");
        }

        [Test]
        public void CampaignBoard_MechanicRamp_NothingAppearsBeforeItsLevel()
        {
            // §5.8: L1 A+B · L2 +C · L3 burst · L4 air · L5 Hard · L6 bombs · L8 Steel.
            AssertAbsent(1, 'P'); AssertAbsent(1, 'H'); AssertAbsent(1, 'X'); AssertAbsent(1, 'S');
            AssertAbsent(2, 'P'); AssertAbsent(2, 'H'); AssertAbsent(2, 'X'); AssertAbsent(2, 'S');
            AssertAbsent(3, 'P'); AssertAbsent(3, 'H'); AssertAbsent(3, 'X'); AssertAbsent(3, 'S');
            AssertAbsent(4, 'H'); AssertAbsent(4, 'X'); AssertAbsent(4, 'S');
            AssertAbsent(5, 'X'); AssertAbsent(5, 'S');
            AssertAbsent(6, 'S');
            AssertAbsent(7, 'S');
            AssertAbsent(1, 'D'); AssertAbsent(2, 'D'); AssertAbsent(3, 'D'); // R4: diamonds start at level 4

            static void AssertAbsent(int level, char forbidden)
            {
                foreach (string row in StrateGenerator.CampaignBoard(level))
                    foreach (char c in row)
                        Assert.AreNotEqual(forbidden, c,
                            $"Level {level} must not contain '{forbidden}' — it is introduced later");
            }
        }

        [Test]
        public void CampaignBoard_Level1_UsesTwoColorsOnly()
        {
            foreach (string row in StrateGenerator.CampaignBoard(1))
                foreach (char c in row)
                    Assert.AreNotEqual('C', c, "Level 1 is A+B only; the third color arrives at level 2");
        }

        [Test]
        public void CampaignBoard_RowCounts_MatchTheDesignTable()
        {
            // CLAUDE.md §9 total rows per level (tripled for v3 pacing — see §15.1).
            int[] expected = { 24, 30, 32, 40, 44, 50, 54, 60, 68, 76 };
            for (int level = 1; level <= 10; level++)
                Assert.AreEqual(expected[level - 1], StrateGenerator.CampaignBoard(level).Length,
                    $"Level {level} row count must match the §9 table");
        }

        // ── Diamonds (R4, §9) ────────────────────────────────────────────────

        [Test]
        public void DiamondCountForLevel_MatchesTheDesignTable()
        {
            int[] expected = { 0, 0, 0, 2, 3, 3, 4, 4, 5, 5 };
            for (int level = 1; level <= 10; level++)
                Assert.AreEqual(expected[level - 1], StrateGenerator.DiamondCountForLevel(level),
                    $"Level {level} diamond count must match the §9 table");
        }

        [Test]
        public void CampaignBoard_Level4Plus_DiamondCount_MatchesTheBoard()
        {
            for (int level = 4; level <= 10; level++)
            {
                string[] board = StrateGenerator.CampaignBoard(level);
                int found = 0;
                foreach (string row in board)
                    foreach (char c in row)
                        if (c == 'D') found++;

                Assert.AreEqual(StrateGenerator.DiamondCountForLevel(level), found,
                    $"Level {level} must place exactly its §9 diamond count on the board");
            }
        }

        [Test]
        public void PlaceDiamonds_NeverPlacesInSpawnOrFloorRows()
        {
            // A board saturated with drillable cells everywhere, so candidates are never the
            // limiting factor — any placement outside [SpawnRows, lastContentRow] is a bug.
            var board = new[]
            {
                ".......", // spawn
                ".......", // spawn
                ".......", // spawn
                "AAAAAAA",
                "AAAAAAA",
                "AAAAAAA",
                "AAAAAAA", // floor
            };
            var rng = new System.Random(1);

            StrateGenerator.PlaceDiamonds(board, count: 21, rng); // every content cell (3 rows × 7)

            for (int x = 0; x < 7; x++)
            {
                Assert.AreNotEqual('D', board[0][x], "row 0 is the spawn zone");
                Assert.AreNotEqual('D', board[1][x], "row 1 is the spawn zone");
                Assert.AreNotEqual('D', board[2][x], "row 2 is the spawn zone");
                Assert.AreNotEqual('D', board[6][x], "row 6 is the bedrock floor");
            }

            for (int y = 3; y <= 5; y++)
                for (int x = 0; x < 7; x++)
                    Assert.AreEqual('D', board[y][x], $"content cell ({x},{y}) should have been filled");
        }

        [Test]
        public void PlaceDiamonds_OnlyReplacesDrillableColorOrHardCells()
        {
            // Steel, Bomb and Empty must never become candidates — R4/§9: "never behind walls".
            var board = new[]
            {
                ".......",
                ".......",
                ".......",
                "SX.HBC.", // Steel, Bomb, Empty, Hard, ColorB, ColorC, Empty
                "AAAAAAA",
            };
            var rng = new System.Random(2);

            // Ask for far more than the 3 eligible cells (H, B, C) — must cap, not overflow onto S/X/'.'.
            StrateGenerator.PlaceDiamonds(board, count: 10, rng);

            Assert.AreEqual('S', board[3][0], "Steel must never be overwritten");
            Assert.AreEqual('X', board[3][1], "Bomb must never be overwritten");
            Assert.AreEqual('.', board[3][2], "Empty must never be overwritten");
            Assert.AreEqual('.', board[3][6], "Empty must never be overwritten");

            int diamonds = 0;
            foreach (char c in board[3])
                if (c == 'D') diamonds++;
            Assert.AreEqual(3, diamonds, "only the Hard/ColorB/ColorC cells were eligible");
        }

        [Test]
        public void PlaceDiamonds_ZeroCount_LeavesBoardUnchanged()
        {
            var board = new[] { "...", "AAA", "AAA" };
            string before = string.Join("|", board);

            StrateGenerator.PlaceDiamonds(board, count: 0, new System.Random(3));

            Assert.AreEqual(before, string.Join("|", board));
        }

        [Test]
        public void EndlessSegment_CanContainDiamonds_AtTheDesignRate()
        {
            // R4/§6.4: a rare bonus drop, not a gate — just confirm the rate is actually wired in.
            // 8 seeds × 40 rows × 7 cols is enough headroom for a 2% roll to show up reliably.
            bool foundAny = false;
            for (int seed = 0; seed < 8 && !foundAny; seed++)
            {
                string[] board = StrateGenerator.EndlessSegment(seed * 12345, StrateGenerator.DefaultWidth, rows: 40, startDepth: 0);
                foreach (string row in board)
                    if (row.IndexOf('D') >= 0) { foundAny = true; break; }
            }

            Assert.IsTrue(foundAny, "EndlessDiamondRate must actually place diamonds somewhere across 8 seeds");
        }

        // ── Enemy placement (R5.13, §9) ───────────────────────────────────────

        [Test]
        public void PlaceEnemies_Level5_PlacesNone()
        {
            string[] board = StrateGenerator.CampaignBoard(5);

            var enemies = StrateGenerator.PlaceEnemies(board, 5, new System.Random(1));

            CollectionAssert.IsEmpty(enemies, "enemies only start at level 6 (§9)");
        }

        [Test]
        public void PlaceEnemies_Level6_PlacesThreeCrawlers_NoBoomers()
        {
            string[] board = StrateGenerator.CampaignBoard(6);

            var enemies = StrateGenerator.PlaceEnemies(board, 6, new System.Random(1));

            Assert.AreEqual(3, enemies.Count);
            foreach (var e in enemies)
                Assert.AreEqual(EnemyType.Crawler, e.type, "Boomers don't appear until level 7 (§9)");
        }

        [Test]
        public void PlaceEnemies_Level7_PlacesThreeCrawlersAndTwoBoomers()
        {
            string[] board = StrateGenerator.CampaignBoard(7);

            var enemies = StrateGenerator.PlaceEnemies(board, 7, new System.Random(1));

            int crawlers = 0, boomers = 0;
            foreach (var e in enemies)
            {
                if (e.type == EnemyType.Crawler) crawlers++;
                else                             boomers++;
            }

            Assert.AreEqual(5, enemies.Count);
            Assert.AreEqual(3, crawlers);
            Assert.AreEqual(2, boomers);
        }

        [Test]
        public void PlaceEnemies_CountsMatchTheCampaignTable()
        {
            // §9: 6 → 3C, 7 → 3C+2B, 8 → 4C+3B, 9 → 5C+4B, 10 → 6C+5B.
            int[] expectedCrawlers = { 0, 0, 0, 0, 0, 3, 3, 4, 5, 6 };
            int[] expectedBoomers  = { 0, 0, 0, 0, 0, 0, 2, 3, 4, 5 };

            for (int level = 1; level <= 10; level++)
            {
                string[] board = StrateGenerator.CampaignBoard(level);
                var enemies = StrateGenerator.PlaceEnemies(board, level, new System.Random(level));

                int crawlers = 0, boomers = 0;
                foreach (var e in enemies)
                {
                    if (e.type == EnemyType.Crawler) crawlers++;
                    else                             boomers++;
                }

                Assert.AreEqual(expectedCrawlers[level - 1], crawlers, $"Crawler count for level {level}");
                Assert.AreEqual(expectedBoomers[level - 1],  boomers,  $"Boomer count for level {level}");
            }
        }

        [Test]
        public void PlaceEnemies_AlwaysInContentRows_NeverSpawnOrFloor()
        {
            for (int level = 6; level <= 10; level++)
            {
                string[] board = StrateGenerator.CampaignBoard(level);
                var enemies = StrateGenerator.PlaceEnemies(board, level, new System.Random(level * 7));

                int lastContentRow = board.Length - StrateGenerator.FloorRows - 1;
                foreach (var e in enemies)
                {
                    Assert.GreaterOrEqual(e.pos.Y, StrateGenerator.SpawnRows,
                        $"level {level}: an enemy landed in the spawn zone");
                    Assert.LessOrEqual(e.pos.Y, lastContentRow,
                        $"level {level}: an enemy landed on the bedrock floor");
                }
            }
        }

        [Test]
        public void PlaceEnemies_RespectsMinimumRowSpacing()
        {
            for (int level = 6; level <= 10; level++)
            {
                string[] board = StrateGenerator.CampaignBoard(level);
                var enemies = StrateGenerator.PlaceEnemies(board, level, new System.Random(level * 13));

                for (int i = 0; i < enemies.Count; i++)
                {
                    for (int j = i + 1; j < enemies.Count; j++)
                    {
                        int gap = System.Math.Abs(enemies[i].pos.Y - enemies[j].pos.Y);
                        Assert.GreaterOrEqual(gap, StrateGenerator.MinEnemyRowSpacing,
                            $"level {level}: enemies at rows {enemies[i].pos.Y} and {enemies[j].pos.Y} are clustered");
                    }
                }
            }
        }

        [Test]
        public void PlaceEnemies_AlwaysOnASolidColorOrHardCell()
        {
            // Never Empty (would read as floating), never Steel or Bomb (those cells mean something
            // else to the player). The enemy is buried IN the cell — it keeps its own type.
            for (int level = 6; level <= 10; level++)
            {
                string[] board = StrateGenerator.CampaignBoard(level);
                var enemies = StrateGenerator.PlaceEnemies(board, level, new System.Random(level * 29));

                foreach (var e in enemies)
                {
                    char c = board[e.pos.Y][e.pos.X];
                    Assert.IsTrue(c == 'A' || c == 'B' || c == 'C' || c == 'H',
                        $"level {level}: enemy at {e.pos} sits on '{c}', which is not a solid Color/Hard cell");
                }
            }
        }

        [Test]
        public void PlaceEnemies_DoesNotMutateTheBoard()
        {
            // Enemies are ACTORS, not CellTypes (§6.5) — placement is pure, the board is untouched.
            string[] board = StrateGenerator.CampaignBoard(10);
            string before = string.Join("|", board);

            StrateGenerator.PlaceEnemies(board, 10, new System.Random(5));

            Assert.AreEqual(before, string.Join("|", board));
        }

        [Test]
        public void PlaceEnemies_IsDeterministic_ForTheSameSeed()
        {
            string[] board = StrateGenerator.CampaignBoard(9);

            var a = StrateGenerator.PlaceEnemies(board, 9, new System.Random(42));
            var b = StrateGenerator.PlaceEnemies(board, 9, new System.Random(42));

            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].type, b[i].type, $"enemy {i} type differs between two identical runs");
                Assert.AreEqual(a[i].pos,  b[i].pos,  $"enemy {i} position differs between two identical runs");
            }
        }

        // ── v3 tutorials ──────────────────────────────────────────────────────

        [Test]
        public void CampaignBoard_Level2_ContainsStreakVein()
        {
            string[] board = StrateGenerator.CampaignBoard(2);
            int col = board[0].Length / 2;

            for (int y = StrateGenerator.SpawnRows;
                 y < StrateGenerator.SpawnRows + StrateGenerator.StreakTutorialRows;
                 y++)
            {
                Assert.AreEqual('A', board[y][col],
                    $"Row {y} of the spawn column must be part of the unbroken ColorA vein");
            }
        }

        [Test]
        public void CampaignBoard_Level2_StreakVein_IsSupportedAtLoad()
        {
            string[] board = StrateGenerator.CampaignBoard(2);
            int col        = board[0].Length / 2;
            int belowVein  = StrateGenerator.SpawnRows + StrateGenerator.StreakTutorialRows;

            Assert.AreNotEqual('.', board[belowVein][col],
                "an unsupported vein falls before the player reaches it");
        }

        [Test]
        public void CampaignBoard_Level2_StreakVein_IsLongEnoughForFourStreak()
        {
            Assert.GreaterOrEqual(StrateGenerator.StreakTutorialRows, 4,
                "§5.8 asks for 4+ blocks so drilling straight down reaches ×4");
        }

        [Test]
        public void CampaignBoard_Level3_BurstSetup_ChunkRestsOnASingleSupport()
        {
            string[] board = StrateGenerator.CampaignBoard(3);
            int width      = board[0].Length;
            int top        = StrateGenerator.BurstTutorialTop(board.Length);

            // Two rows of chunk, then the support row.
            int chunkCells = 0;
            for (int y = top; y <= top + 1; y++)
                for (int x = 0; x < width; x++)
                    if (board[y][x] == 'C') chunkCells++;

            Assert.GreaterOrEqual(chunkCells, 6, "§5.8 asks for a 6+ cell chunk");

            // Exactly one cell of the support row sits under a chunk cell.
            int supports = 0;
            for (int x = 0; x < width; x++)
                if (board[top + 1][x] == 'C' && board[top + 2][x] != '.') supports++;

            Assert.AreEqual(1, supports, "the chunk must rest on exactly one drillable block");
        }

        [Test]
        public void CampaignBoard_Level3_BurstSetup_HasDropZoneDeepEnoughToBurst()
        {
            string[] board = StrateGenerator.CampaignBoard(3);
            int width      = board[0].Length;
            int top        = StrateGenerator.BurstTutorialTop(board.Length);

            // Under the chunk's overhang (every chunk column except the supported one)
            // there must be 3+ clear rows, so the fall distance clears the burst threshold.
            for (int x = 0; x < width; x++)
            {
                if (board[top + 1][x] != 'C') continue;
                if (board[top + 2][x] != '.') continue; // this is the supported column

                for (int y = top + 2; y <= top + 5; y++)
                    Assert.AreEqual('.', board[y][x],
                        $"drop zone at column {x}, row {y} must be clear");
            }
        }

        [Test]
        public void CampaignBoard_Level3_BurstSetup_SupportIsGroundedAtLoad()
        {
            string[] board = StrateGenerator.CampaignBoard(3);
            int width      = board[0].Length;
            int top        = StrateGenerator.BurstTutorialTop(board.Length);
            int pillarCol  = width - 1;

            // The pillar that grounds the support runs unbroken down to the authored floor row.
            for (int y = top + 2; y <= top + 5; y++)
                Assert.AreNotEqual('.', board[y][pillarCol],
                    $"grounding pillar broken at row {y}");

            foreach (char c in board[top + 6])
                Assert.AreEqual('A', c, "the band ends on a solid authored floor");
        }

        [Test]
        public void CampaignBoard_Level3_BurstSetup_DrillingSupport_ActuallyBursts()
        {
            // End-to-end: run the authored band through the real GravitySystem.
            var grid    = GridModel.FromStringMap(StrateGenerator.CampaignBoard(3));
            var gravity = new GravitySystem(grid) { WobbleDuration = 0.1f, FallStepInterval = 0.02f };
            int top     = StrateGenerator.BurstTutorialTop(grid.Height);

            // Settle first: the generated terrain below the band may shift on its own.
            TestUtil.TickMany(gravity, 3f, new GridPos(0, 0));

            int burstCells = 0, burstDistance = 0;
            gravity.ChunkBurst += (cells, d, _) => { burstCells = cells.Count; burstDistance = d; };

            // Find and drill the chunk's single support.
            int supportCol = -1;
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.Get(new GridPos(x, top + 1)) == CellType.ColorC &&
                    grid.Get(new GridPos(x, top + 2)) != CellType.Empty)
                {
                    supportCol = x;
                    break;
                }
            }
            Assert.GreaterOrEqual(supportCol, 0, "support column must still exist after settling");
            Assert.IsTrue(grid.Drill(new GridPos(supportCol, top + 2)), "the support must be drillable");

            TestUtil.TickMany(gravity, 4f, new GridPos(0, 0));

            Assert.GreaterOrEqual(burstDistance, GravitySystem.BurstFallThreshold,
                "drilling the lone support must produce a burst, not a 1-row settle");
            Assert.GreaterOrEqual(burstCells, 6, "the whole authored chunk should shatter together");
        }

        // ── Tutorial showcase board ───────────────────────────────────────────

        [Test]
        public void TutorialBoard_IsWellFormed_AndDeterministic()
        {
            string[] a = StrateGenerator.TutorialBoard();
            string[] b = StrateGenerator.TutorialBoard();

            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(b[i], a[i], $"Row {i} differs between two calls — must be deterministic");
                Assert.AreEqual(StrateGenerator.DefaultWidth, a[i].Length, $"Row {i} must be width 7");
            }

            // Spawn zone is clear air; the bottom row is solid bedrock.
            for (int y = 0; y < StrateGenerator.SpawnRows; y++)
                Assert.IsFalse(a[y].Any(c => c != '.'), $"Spawn row {y} must be empty");
            Assert.IsTrue(a[a.Length - 1].All(c => c == 'A'), "the board must end on a solid floor");
        }

        [Test]
        public void TutorialBoard_SettlesAtLoad_WithoutBursting()
        {
            // The anchoring frame must hold every lesson piece in place at load — no self-demolition.
            var grid    = GridModel.FromStringMap(StrateGenerator.TutorialBoard());
            var gravity = new GravitySystem(grid) { WobbleDuration = 0.1f, FallStepInterval = 0.02f };

            bool bursted = false;
            gravity.ChunkBurst += (cells, dist, color) => bursted = true;

            gravity.Settle();
            TestUtil.TickMany(gravity, 2f, new GridPos(StrateGenerator.TutorialSpawnColumn, 2));

            Assert.IsFalse(bursted, "a correctly anchored tutorial board must not burst on load");
            Assert.IsFalse(gravity.IsBusy, "everything must be at rest after Settle()");

            // Key lesson pieces survived the settle exactly where they were authored.
            Assert.AreEqual(CellType.ColorC, grid.Get(new GridPos(0, StrateGenerator.TutorialPerfectClearRow)),
                "the Perfect Clear slab dissolved or shifted on load");
            Assert.AreEqual(CellType.Bomb, grid.Get(new GridPos(StrateGenerator.TutorialSpawnColumn, StrateGenerator.TutorialBombRow)),
                "the center bomb moved on load");
        }

        [Test]
        public void TutorialBoard_StreakChamber_YieldsSixStreak()
        {
            var grid   = GridModel.FromStringMap(StrateGenerator.TutorialBoard());
            var streak = new StreakTracker();
            int col    = StrateGenerator.TutorialSpawnColumn;

            // Drill straight down the vein, as the natural first instinct does. Stops at the
            // Diamond chamber (row 9), not the Perfect Clear row — the vein itself is only rows 3-8.
            for (int y = StrateGenerator.SpawnRows; y < StrateGenerator.TutorialDiamondRow; y++)
                streak.NotifyDrill(grid.Get(new GridPos(col, y)), DrillDirection.Down);

            Assert.AreEqual(6, streak.CurrentStreak, "the ColorB vein must build an unbroken ×6 streak");
            Assert.AreEqual(CellType.ColorB, streak.CurrentColor);
        }

        [Test]
        public void TutorialBoard_DiamondChamber_SurvivesSettle_AtAuthoredPositions()
        {
            // Regression guard for the §5.2 "unanchored structure" trap: each diamond must rest on
            // its own solid support (not empty air), or Settle() silently drops it onto the Perfect
            // Clear slab below, wrecking the authored layout before the player ever sees it.
            var grid    = GridModel.FromStringMap(StrateGenerator.TutorialBoard());
            var gravity = new GravitySystem(grid) { WobbleDuration = 0.1f, FallStepInterval = 0.02f };

            int onPathCol  = StrateGenerator.TutorialSpawnColumn;
            int offPathCol = 1; // authored off-path column (§5.14)
            int row        = StrateGenerator.TutorialDiamondRow;

            gravity.Settle();

            Assert.AreEqual(CellType.Diamond, grid.Get(new GridPos(onPathCol, row)),
                "the on-path diamond must stay put at load, not fall onto the slab below");
            Assert.AreEqual(CellType.Diamond, grid.Get(new GridPos(offPathCol, row + 1)),
                "the off-path diamond must stay put at load, not fall onto the slab below");
        }

        [Test]
        public void TutorialBoard_DiamondChamber_CollectingBothDiamonds_CompletesDiamondSystem()
        {
            // End-to-end demonstration of the R4 mechanic: drill both authored diamonds, feed
            // DiamondSystem the way GameBootstrap's Drilled handler does, and confirm the pickup
            // completes exactly like it would in a real run (§6.4).
            var grid = GridModel.FromStringMap(StrateGenerator.TutorialBoard());

            var onPath  = new GridPos(StrateGenerator.TutorialSpawnColumn, StrateGenerator.TutorialDiamondRow);
            var offPath = new GridPos(1, StrateGenerator.TutorialDiamondRow + 1);

            Assert.AreEqual(CellType.Diamond, grid.Get(onPath));
            Assert.AreEqual(CellType.Diamond, grid.Get(offPath));

            var diamonds = new DiamondSystem();
            diamonds.Init(2);

            Assert.IsTrue(grid.Drill(onPath));
            diamonds.NotifyCollected(onPath);
            Assert.IsFalse(diamonds.IsComplete, "one diamond collected, one still to go");

            Assert.IsTrue(grid.Drill(offPath));
            diamonds.NotifyCollected(offPath);

            Assert.AreEqual(2, diamonds.Collected);
            Assert.IsTrue(diamonds.IsComplete, "both authored diamonds collected");
            Assert.AreEqual(CellType.Empty, grid.Get(onPath));
            Assert.AreEqual(CellType.Empty, grid.Get(offPath));
        }

        [Test]
        public void TutorialBoard_BurstChamber_DrillingSupport_ActuallyBursts()
        {
            // End-to-end against the real GravitySystem: drilling the lone support shatters the chunk.
            var grid    = GridModel.FromStringMap(StrateGenerator.TutorialBoard());
            var gravity = new GravitySystem(grid) { WobbleDuration = 0.1f, FallStepInterval = 0.02f };
            gravity.Settle();

            int top = StrateGenerator.TutorialBurstChunkTop;

            int burstCells = 0, burstDistance = 0;
            gravity.ChunkBurst += (cells, d, _) => { burstCells = cells.Count; burstDistance = d; };

            // The chunk's single support: a solid cell directly under a chunk-bottom ColorC cell.
            int supportCol = -1;
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.Get(new GridPos(x, top + 1)) == CellType.ColorC &&
                    grid.Get(new GridPos(x, top + 2)) != CellType.Empty)
                {
                    supportCol = x;
                    break;
                }
            }
            Assert.GreaterOrEqual(supportCol, 0, "the chunk must rest on exactly one support");
            Assert.IsTrue(grid.Drill(new GridPos(supportCol, top + 2)), "the support must be drillable");

            TestUtil.TickMany(gravity, 4f, new GridPos(0, 0));

            Assert.GreaterOrEqual(burstDistance, GravitySystem.BurstFallThreshold,
                "drilling the support must produce a burst, not a 1-row settle");
            Assert.GreaterOrEqual(burstCells, 6, "the whole 10-cell chunk should shatter together");
        }

        [Test]
        public void TutorialBoard_BombChamber_ArmingCenter_ChainsToThree()
        {
            var grid     = GridModel.FromStringMap(StrateGenerator.TutorialBoard());
            var collapse = new CollapseSystem(grid);
            var bombs    = new BombSystem(grid, collapse);

            int maxChain = 0;
            bombs.BombScored += (destroyed, chainMult) => { if (chainMult > maxChain) maxChain = chainMult; };

            int row = StrateGenerator.TutorialBombRow;
            int col = StrateGenerator.TutorialSpawnColumn;
            Assert.AreEqual(CellType.Bomb, grid.Get(new GridPos(col, row)), "the center bomb must exist");

            // Arm the center bomb the way the player does: by drilling the cell right above it.
            // v3.2 (§5.3): this bomb gets DirectBlastRadius = 1 — it reaches the second bomb stacked
            // directly beneath it (distance 1), whose own ChainBlastRadius = 2 then reaches the third.
            bombs.NotifyDrilled(new GridPos(col, row - 1));

            const float step = 1f / 60f;
            for (float t = 0f; t < 3f; t += step)
                bombs.Tick(step, new GridPos(0, 0)); // avatar far away — measure payout, not damage

            Assert.GreaterOrEqual(maxChain, 3, "the three-bomb stack must detonate sympathetically to ×3");
        }

        [Test]
        public void TutorialBoard_PerfectClearSecret_ClearingSlab_Collapses()
        {
            var grid     = GridModel.FromStringMap(StrateGenerator.TutorialBoard());
            var collapse = new CollapseSystem(grid);
            int row      = StrateGenerator.TutorialPerfectClearRow;

            // The secret is a full-width, fully drillable slab.
            for (int x = 0; x < grid.Width; x++)
                Assert.AreEqual(CellType.ColorC, grid.Get(new GridPos(x, row)),
                    $"slab cell {x} must be a drillable ColorC block");

            int collapsedRow = -1;
            collapse.PerfectClear += r => collapsedRow = r;

            // Clear the entire row, then resolve.
            for (int x = 0; x < grid.Width; x++)
                Assert.IsTrue(grid.Drill(new GridPos(x, row)), $"slab cell {x} must drill");

            int count = collapse.Resolve(new GridPos(0, 0));

            Assert.AreEqual(1, count, "the fully cleared slab must collapse exactly once");
            Assert.AreEqual(row, collapsedRow, "Perfect Clear must fire for the slab row");
        }

        [Test]
        public void CampaignBoard_IsDeterministic()
        {
            string[] a = StrateGenerator.CampaignBoard(5);
            string[] b = StrateGenerator.CampaignBoard(5);

            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
                Assert.AreEqual(a[i], b[i], $"Row {i} differs between two calls");
        }

        [Test]
        public void EndlessBoard_IsDeterministic()
        {
            string[] a = StrateGenerator.EndlessBoard(seed: 12345, width: StrateGenerator.DefaultWidth, depth: 20);
            string[] b = StrateGenerator.EndlessBoard(seed: 12345, width: StrateGenerator.DefaultWidth, depth: 20);

            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
                Assert.AreEqual(a[i], b[i], $"Row {i} differs between two calls");
        }

        [Test]
        public void EndlessBoard_ContentRows_HavePorosity()
        {
            // Regression guard: EndlessStrates once passed emptyRate = 0f, producing solid boards
            // with no gaps — chunks could never fall 2+ rows, so Chunk Burst was dead in endless.
            // A real board must have empty cells threaded through its content rows.
            string[] board = StrateGenerator.EndlessBoard(seed: 777, width: StrateGenerator.DefaultWidth, depth: 60);

            // Content rows only: skip the empty spawn zone at the top and the solid floor at the bottom.
            int firstContent = StrateGenerator.SpawnRows;
            int lastContent  = board.Length - StrateGenerator.FloorRows - 1;

            int emptyCells   = 0;
            int contentCells = 0;
            for (int y = firstContent; y <= lastContent; y++)
            {
                foreach (char c in board[y])
                {
                    contentCells++;
                    if (c == '.') emptyCells++;
                }
            }

            Assert.Greater(contentCells, 0, "Board should have content rows to inspect");
            // Porosity ramps ~0.28→0.36; over a 60-deep board the empty share must land well clear
            // of zero. A loose 10% floor makes this robust to seed variance while still failing hard
            // if emptyRate regresses to 0.
            float emptyShare = (float)emptyCells / contentCells;
            Assert.Greater(emptyShare, 0.10f,
                $"Endless content is too dense (empty share {emptyShare:P0}) — emptyRate likely regressed toward 0");
        }

        [Test]
        public void EndlessSegment_AtStartDepthZero_MatchesEndlessBoard()
        {
            string[] board   = StrateGenerator.EndlessBoard(seed: 55, width: StrateGenerator.DefaultWidth, depth: 30);
            string[] segment = StrateGenerator.EndlessSegment(seed: 55, width: StrateGenerator.DefaultWidth, rows: 30, startDepth: 0);

            CollectionAssert.AreEqual(board, segment, "the first segment of a run IS an endless board");
        }

        [Test]
        public void EndlessSegment_ResumesTheDifficultyRamp_InsteadOfRestartingIt()
        {
            // A run is a chain of finite boards (a GridModel has a fixed height). If each segment
            // restarted the ramp, the well would get EASIER the deeper the player dug.
            // Averaged over several seeds so this measures the ramp, not seed variance.
            float shallow = AverageHazardShare(startDepth: 0);
            float deep    = AverageHazardShare(startDepth: 200); // past the 80-row ramp: steady-state max

            Assert.Greater(deep, shallow * 1.5f,
                $"deep segments must be harder (hazard share {deep:P0} vs {shallow:P0}) — the ramp is restarting");
        }

        [Test]
        public void EndlessSegment_AtSteadyState_KeepsColorMaterialForStreaks()
        {
            // R3.1 guard. Color blocks are the material of the core loop — every drill pays ×streak
            // (design rule 1). The pre-R3.1 caps (0.15 hard / 0.12 steel) starved it: measured, deep
            // endless was 22 % color against campaign 10's 32 %, i.e. two thirds of every solid cell
            // was hazard exactly where the run is supposed to peak. Endless should END harder than
            // the campaign, not stop being the same game.
            float color = AverageShare(startDepth: 160, "ABC");
            Assert.Greater(color, 0.24f,
                $"deep endless has only {color:P0} color cells — the streak system has no material left");
        }

        [Test]
        public void EndlessSegment_AtSteadyState_KeepsBurstOpportunity()
        {
            // R3.1 guard. A burst needs TWO stacked gaps under a chunk, so opportunity scales
            // ~EmptyRate². Before the retune the deepest boards offered the FEWEST bursts (~11 % of
            // solid cells vs the campaign's 12-15 %) — backwards for the mode whose payoff is
            // spectacle (design rule 2).
            float ready = AverageBurstReadyShare(startDepth: 160);
            Assert.Greater(ready, 0.11f,
                $"deep endless offers burst opportunity on only {ready:P0} of solid cells");
        }

        /// <summary>Share of Hard/Steel/Bomb cells in the content rows, averaged over 8 seeds.</summary>
        private static float AverageHazardShare(int startDepth) => AverageShare(startDepth, "HSX");

        /// <summary>Share of content cells whose character is in <paramref name="chars"/>, over 8 seeds.</summary>
        private static float AverageShare(int startDepth, string chars)
        {
            int hits = 0, cells = 0;
            foreach (string[] board in EndlessSamples(startDepth))
            {
                for (int y = StrateGenerator.SpawnRows; y <= board.Length - StrateGenerator.FloorRows - 1; y++)
                {
                    foreach (char c in board[y])
                    {
                        cells++;
                        if (chars.IndexOf(c) >= 0) hits++;
                    }
                }
            }
            return (float)hits / cells;
        }

        /// <summary>Share of SOLID cells that could actually burst — 2+ empty cells stacked beneath them.</summary>
        private static float AverageBurstReadyShare(int startDepth)
        {
            int ready = 0, solid = 0;
            foreach (string[] board in EndlessSamples(startDepth))
            {
                int last = board.Length - StrateGenerator.FloorRows - 1;
                for (int y = StrateGenerator.SpawnRows; y <= last; y++)
                {
                    for (int x = 0; x < board[y].Length; x++)
                    {
                        if (board[y][x] == '.') continue;
                        solid++;

                        int gap = 0;
                        for (int d = 1; d <= GravitySystem.BurstFallThreshold && y + d <= last; d++)
                        {
                            if (board[y + d][x] != '.') break;
                            gap++;
                        }
                        if (gap >= GravitySystem.BurstFallThreshold) ready++;
                    }
                }
            }
            return (float)ready / solid;
        }

        /// <summary>Eight seeds of 40-row endless content at the given depth — enough to average out seed variance.</summary>
        private static IEnumerable<string[]> EndlessSamples(int startDepth)
        {
            for (int seed = 0; seed < 8; seed++)
                yield return StrateGenerator.EndlessSegment(seed * 7919, StrateGenerator.DefaultWidth, rows: 40, startDepth: startDepth);
        }
    }

    [TestFixture]
    public class CampaignManagerTests
    {
        // ── Initial state ────────────────────────────────────────────────────

        [Test]
        public void Campaign_InitialLevel_IsOne()
        {
            var c = new CampaignManager();
            Assert.AreEqual(1, c.CurrentLevel);
        }

        [Test]
        public void Campaign_InitialComplete_IsFalse()
        {
            var c = new CampaignManager();
            Assert.IsFalse(c.IsCampaignComplete);
        }

        // ── Win condition ────────────────────────────────────────────────────

        [Test]
        public void Campaign_NotifyPosition_AboveThreshold_NoEvent()
        {
            var c = new CampaignManager();
            string[] board = c.BuildCurrentBoard();
            int boardHeight = board.Length;
            int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

            bool fired = false;
            c.LevelCompleted += _ => fired = true;

            // One row above the threshold — should not fire.
            c.NotifyAvatarPosition(new GridPos(4, threshold - 1), boardHeight);

            Assert.IsFalse(fired);
        }

        [Test]
        public void Campaign_NotifyPosition_AtThreshold_FiresLevelCompleted()
        {
            var c = new CampaignManager();
            string[] board = c.BuildCurrentBoard();
            int boardHeight = board.Length;
            int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

            int firedLevel = -1;
            c.LevelCompleted += lvl => firedLevel = lvl;

            c.NotifyAvatarPosition(new GridPos(4, threshold), boardHeight);

            Assert.AreEqual(1, firedLevel, "LevelCompleted must fire with CurrentLevel");
        }

        [Test]
        public void Campaign_LevelCompleted_FiredOnceOnly()
        {
            var c = new CampaignManager();
            string[] board = c.BuildCurrentBoard();
            int boardHeight = board.Length;
            int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

            int count = 0;
            c.LevelCompleted += _ => count++;

            // Notify the same position multiple times in a row.
            c.NotifyAvatarPosition(new GridPos(4, threshold), boardHeight);
            c.NotifyAvatarPosition(new GridPos(4, threshold), boardHeight);
            c.NotifyAvatarPosition(new GridPos(4, threshold), boardHeight);

            Assert.AreEqual(1, count, "LevelCompleted must fire exactly once per level");
        }

        [Test]
        public void Campaign_LevelCompleted_EventArg_IsCurrentLevel()
        {
            var c = new CampaignManager(startLevel: 5);
            string[] board = c.BuildCurrentBoard();
            int boardHeight = board.Length;
            int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

            int received = -1;
            c.LevelCompleted += lvl => received = lvl;

            c.NotifyAvatarPosition(new GridPos(0, threshold), boardHeight);

            Assert.AreEqual(5, received);
        }

        // ── Score gate (v3.1 arcade pivot, §5.7 — replaces the R4 diamond gate) ──

        [Test]
        public void Campaign_AtThreshold_ScoreBelowMinimum_DoesNotComplete()
        {
            var c = new CampaignManager(startLevel: 4);
            string[] board = c.BuildCurrentBoard();
            int boardHeight = board.Length;
            int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

            bool fired = false;
            c.LevelCompleted += _ => fired = true;

            c.NotifyAvatarPosition(new GridPos(0, threshold), boardHeight, currentScore: 0);

            Assert.IsFalse(fired, "reaching the bottom is not enough while the score gate is unmet");
        }

        [Test]
        public void Campaign_AtThreshold_ScoreMeetsMinimum_FiresLevelCompleted()
        {
            var c = new CampaignManager(startLevel: 4);
            string[] board = c.BuildCurrentBoard();
            int boardHeight = board.Length;
            int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

            bool fired = false;
            c.LevelCompleted += _ => fired = true;

            // Level 4's minimum is exactly 500 (§9).
            c.NotifyAvatarPosition(new GridPos(0, threshold), boardHeight, currentScore: 500);

            Assert.IsTrue(fired, "meeting the score minimum at the bottom must complete the level");
        }

        [Test]
        public void Campaign_AtThreshold_ScoreRisesAboveMinimumAfterward_ThenFiresLevelCompleted()
        {
            var c = new CampaignManager(startLevel: 4);
            string[] board = c.BuildCurrentBoard();
            int boardHeight = board.Length;
            int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

            bool fired = false;
            c.LevelCompleted += _ => fired = true;

            // Standing at the bottom, still short of the gate.
            c.NotifyAvatarPosition(new GridPos(0, threshold), boardHeight, currentScore: 200);
            Assert.IsFalse(fired);

            // Score caught up (a late burst/bomb/diamond) — same frame loop re-checks and the gate opens.
            c.NotifyAvatarPosition(new GridPos(0, threshold), boardHeight, currentScore: 500);
            Assert.IsTrue(fired, "the score gate opening while at the bottom must complete the level");
        }

        [Test]
        public void Campaign_ScoreZero_Level1_StillWins_NoGateOnTutorialLevels()
        {
            var c = new CampaignManager(startLevel: 1);
            string[] board = c.BuildCurrentBoard();
            int boardHeight = board.Length;
            int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

            bool fired = false;
            c.LevelCompleted += _ => fired = true;

            c.NotifyAvatarPosition(new GridPos(0, threshold), boardHeight, currentScore: 0);

            Assert.IsTrue(fired, "levels 1-3 have a 0 minimum — depth alone must win");
        }

        [Test]
        public void Campaign_NotAtThreshold_HighScore_StillDoesNotComplete()
        {
            // Depth is always required (design rule 6) — no amount of score skips the floor.
            var c = new CampaignManager(startLevel: 4);
            string[] board = c.BuildCurrentBoard();
            int boardHeight = board.Length;
            int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

            bool fired = false;
            c.LevelCompleted += _ => fired = true;

            c.NotifyAvatarPosition(new GridPos(0, threshold - 1), boardHeight, currentScore: 10000);

            Assert.IsFalse(fired, "score cannot substitute for depth");
        }

        [Test]
        public void Campaign_CurrentScoreDefaultsToMaxValue_UnaffectedCallersStillWinOnDepthAlone()
        {
            // Callers that don't care about the gate (tutorial showcase, older tests) never pass a
            // score — even on a level with a real minimum, the default must clear it.
            var c = new CampaignManager(startLevel: 4);
            string[] board = c.BuildCurrentBoard();
            int boardHeight = board.Length;
            int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

            bool fired = false;
            c.LevelCompleted += _ => fired = true;

            c.NotifyAvatarPosition(new GridPos(0, threshold), boardHeight);

            Assert.IsTrue(fired);
        }

        [Test]
        public void ScoreMinimumForLevel_MatchesTheCampaignTable()
        {
            // §9: 1-3 → 0, 4-5 → 500, 6-7 → 1500, 8-9 → 3000, 10 → 5000.
            Assert.AreEqual(0,    CampaignManager.ScoreMinimumForLevel(1));
            Assert.AreEqual(0,    CampaignManager.ScoreMinimumForLevel(2));
            Assert.AreEqual(0,    CampaignManager.ScoreMinimumForLevel(3));
            Assert.AreEqual(500,  CampaignManager.ScoreMinimumForLevel(4));
            Assert.AreEqual(500,  CampaignManager.ScoreMinimumForLevel(5));
            Assert.AreEqual(1500, CampaignManager.ScoreMinimumForLevel(6));
            Assert.AreEqual(1500, CampaignManager.ScoreMinimumForLevel(7));
            Assert.AreEqual(3000, CampaignManager.ScoreMinimumForLevel(8));
            Assert.AreEqual(3000, CampaignManager.ScoreMinimumForLevel(9));
            Assert.AreEqual(5000, CampaignManager.ScoreMinimumForLevel(10));
        }

        // ── Depth-only win (v3: the line gate is gone) ───────────────────────

        [Test]
        public void Campaign_ReachingTheBottom_CompletesLevel_OnEveryLevel()
        {
            // No level gates on anything but depth now — verify across the whole ramp.
            for (int level = 1; level <= CampaignManager.LevelCount; level++)
            {
                var c = new CampaignManager(level);
                string[] board = c.BuildCurrentBoard();
                int boardHeight = board.Length;
                int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

                bool fired = false;
                c.LevelCompleted += _ => fired = true;

                c.NotifyAvatarPosition(new GridPos(0, threshold), boardHeight);

                Assert.IsTrue(fired, $"Level {level} must complete on depth alone");
            }
        }

        [Test]
        public void Campaign_AboveThreshold_DoesNotCompleteLevel()
        {
            for (int level = 1; level <= CampaignManager.LevelCount; level++)
            {
                var c = new CampaignManager(level);
                string[] board = c.BuildCurrentBoard();
                int boardHeight = board.Length;
                int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

                bool fired = false;
                c.LevelCompleted += _ => fired = true;

                c.NotifyAvatarPosition(new GridPos(0, threshold - 1), boardHeight);

                Assert.IsFalse(fired, $"Level {level} must not complete one row short of the exit");
            }
        }

        [Test]
        public void Campaign_BelowThreshold_AlsoCompletesLevel()
        {
            var c = new CampaignManager(startLevel: 4);
            string[] board = c.BuildCurrentBoard();
            int boardHeight = board.Length;
            int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

            bool fired = false;
            c.LevelCompleted += _ => fired = true;

            c.NotifyAvatarPosition(new GridPos(0, threshold + 1), boardHeight);

            Assert.IsTrue(fired, "deeper than the exit still counts as reaching the bottom");
        }

        [Test]
        public void Campaign_DrainRate_Ramp()
        {
            Assert.AreEqual(4f, CampaignManager.DrainRateForLevel(1));
            Assert.AreEqual(4f, CampaignManager.DrainRateForLevel(3));
            Assert.AreEqual(7f, CampaignManager.DrainRateForLevel(4), "§15.1: drain bites once runs are long enough");
            Assert.AreEqual(7f, CampaignManager.DrainRateForLevel(10));
        }

        [Test]
        public void Campaign_WobbleDuration_Ramp()
        {
            Assert.AreEqual(0.8f, CampaignManager.WobbleDurationForLevel(1));
            Assert.AreEqual(0.8f, CampaignManager.WobbleDurationForLevel(3));
            Assert.AreEqual(0.6f, CampaignManager.WobbleDurationForLevel(4), "GDD wobble from level 4 on");
            Assert.AreEqual(0.6f, CampaignManager.WobbleDurationForLevel(10));
        }

        [Test]
        public void Campaign_RestartLevel_ClearsWinGuard_WithoutChangingLevel()
        {
            var c = new CampaignManager(startLevel: 2);
            string[] board = c.BuildCurrentBoard();
            int boardHeight = board.Length;
            int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

            int count = 0;
            c.LevelCompleted += _ => count++;

            c.NotifyAvatarPosition(new GridPos(0, threshold), boardHeight);
            c.RestartLevel();
            c.NotifyAvatarPosition(new GridPos(0, threshold), boardHeight);

            Assert.AreEqual(2, count, "the win must be detectable again after a restart");
            Assert.AreEqual(2, c.CurrentLevel, "Restart must not change the level");
        }

        // ── Advance ──────────────────────────────────────────────────────────

        [Test]
        public void Campaign_Advance_IncrementsLevel()
        {
            var c = new CampaignManager(startLevel: 3);
            c.Advance();
            Assert.AreEqual(4, c.CurrentLevel);
        }

        [Test]
        public void Campaign_Advance_Level10_FiresCampaignCompleted()
        {
            var c = new CampaignManager(startLevel: 10);
            bool fired = false;
            c.CampaignCompleted += () => fired = true;

            c.Advance();

            Assert.IsTrue(fired);
            Assert.IsTrue(c.IsCampaignComplete);
        }

        [Test]
        public void Campaign_Advance_BeyondLevel10_IsNoOp()
        {
            var c = new CampaignManager(startLevel: 10);
            c.Advance(); // → complete

            int completedCount = 0;
            c.CampaignCompleted += () => completedCount++;
            c.Advance(); // should be ignored

            Assert.AreEqual(10, c.CurrentLevel);
            Assert.AreEqual(0, completedCount, "CampaignCompleted must not fire again");
        }

        // ── Reset ────────────────────────────────────────────────────────────

        [Test]
        public void Campaign_Reset_ResetsToLevel1()
        {
            var c = new CampaignManager(startLevel: 7);
            c.Advance();
            c.Reset();

            Assert.AreEqual(1, c.CurrentLevel);
            Assert.IsFalse(c.IsCampaignComplete);
        }

        [Test]
        public void Campaign_Reset_AllowsWinAgain()
        {
            var c = new CampaignManager();
            string[] board = c.BuildCurrentBoard();
            int boardHeight = board.Length;
            int threshold   = boardHeight - CampaignManager.WinDepthFromFloor;

            int count = 0;
            c.LevelCompleted += _ => count++;

            c.NotifyAvatarPosition(new GridPos(0, threshold), boardHeight);
            c.Reset();
            c.NotifyAvatarPosition(new GridPos(0, threshold), boardHeight);

            Assert.AreEqual(2, count, "Win condition must be detectable again after Reset");
        }

        // ── BuildCurrentBoard ────────────────────────────────────────────────

        [Test]
        public void Campaign_BuildCurrentBoard_ParsesByGridModel()
        {
            for (int level = 1; level <= CampaignManager.LevelCount; level++)
            {
                var c = new CampaignManager(level);
                string[] board = c.BuildCurrentBoard();
                Assert.DoesNotThrow(() => GridModel.FromStringMap(board),
                    $"Level {level} board must be parseable by GridModel");
            }
        }

        [Test]
        public void Campaign_BuildCurrentBoard_IsDeterministic()
        {
            var c = new CampaignManager(startLevel: 6);
            string[] a = c.BuildCurrentBoard();
            string[] b = c.BuildCurrentBoard();

            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
                Assert.AreEqual(a[i], b[i], $"Row {i} differs between two calls for level 6");
        }
    }

    [TestFixture]
    public class StreakTrackerTests
    {
        // ── Building a streak ───────────────────────────────────────────────

        [Test]
        public void SameColorThreeTimes_StreakReachesThree()
        {
            var streak = new StreakTracker();

            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);

            Assert.AreEqual(3, streak.CurrentStreak);
            Assert.AreEqual(CellType.ColorA, streak.CurrentColor);
        }

        [Test]
        public void DifferentColor_ResetsStreakToOne()
        {
            var streak = new StreakTracker();

            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorB, DrillDirection.Down);

            Assert.AreEqual(1, streak.CurrentStreak);
            Assert.AreEqual(CellType.ColorB, streak.CurrentColor);
        }

        // ── Streak-neutral cells ────────────────────────────────────────────

        [Test]
        public void AirCapsule_IsStreakNeutral_DoesNotBreakOrGrow()
        {
            var streak = new StreakTracker();

            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.AirCapsule, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);

            Assert.AreEqual(2, streak.CurrentStreak, "capsule must not count toward or break the streak");
            Assert.AreEqual(CellType.ColorA, streak.CurrentColor);
        }

        [Test]
        public void Hard_IsStreakNeutral_DoesNotBreakOrGrow()
        {
            var streak = new StreakTracker();

            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.Hard, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);

            Assert.AreEqual(2, streak.CurrentStreak, "hard block must not count toward or break the streak");
        }

        [Test]
        public void HardCracked_IsStreakNeutral_DoesNotBreakOrGrow()
        {
            var streak = new StreakTracker();

            streak.NotifyDrill(CellType.ColorB, DrillDirection.Down);
            streak.NotifyDrill(CellType.HardCracked, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorB, DrillDirection.Down);

            Assert.AreEqual(2, streak.CurrentStreak);
        }

        [Test]
        public void Diamond_IsStreakNeutral_DoesNotBreakOrGrow()
        {
            var streak = new StreakTracker();

            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.Diamond, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);

            Assert.AreEqual(2, streak.CurrentStreak, "diamond must not count toward or break the streak");
            Assert.AreEqual(CellType.ColorA, streak.CurrentColor);
        }

        // ── v3.1 arcade pivot: vertical-only streak ─────────────────────────

        [Test]
        public void LateralDrill_SameColor_DoesNotGrowStreak()
        {
            var streak = new StreakTracker();

            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Left);

            Assert.AreEqual(2, streak.CurrentStreak, "a lateral drill must not grow the streak, even same-color");
            Assert.AreEqual(CellType.ColorA, streak.CurrentColor);
        }

        [Test]
        public void UpwardDrill_DifferentColor_DoesNotResetStreak()
        {
            var streak = new StreakTracker();

            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorB, DrillDirection.Up);

            Assert.AreEqual(2, streak.CurrentStreak, "an upward drill must not break the streak, even a different color");
            Assert.AreEqual(CellType.ColorA, streak.CurrentColor);
        }

        [Test]
        public void DownwardDrill_SameColor_GrowsStreakNormally()
        {
            var streak = new StreakTracker();

            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);

            Assert.AreEqual(2, streak.CurrentStreak);
            Assert.AreEqual(CellType.ColorA, streak.CurrentColor);
        }

        // ── Events ───────────────────────────────────────────────────────────

        [Test]
        public void StreakBroken_FiresWithCountThatJustEnded()
        {
            var streak = new StreakTracker();
            int? broken = null;
            streak.StreakBroken += count => broken = count;

            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            Assert.IsNull(broken, "must not fire while the streak is still alive");

            streak.NotifyDrill(CellType.ColorB, DrillDirection.Down);
            Assert.AreEqual(2, broken, "must report the streak length that just ended (AA), not the new one");
        }

        [Test]
        public void StreakBroken_DoesNotFireOnFirstEverDrill()
        {
            var streak = new StreakTracker();
            bool broken = false;
            streak.StreakBroken += _ => broken = true;

            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);

            Assert.IsFalse(broken, "there is no prior streak to break on the very first drill");
        }

        [Test]
        public void StreakGrew_FiresOnEveryColorDrill_WithRunningCount()
        {
            var streak = new StreakTracker();
            var counts = new List<int>();
            streak.StreakGrew += counts.Add;

            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, counts);
        }

        [Test]
        public void StreakGrew_DoesNotFireOnNeutralCells()
        {
            var streak = new StreakTracker();
            int growCount = 0;
            streak.StreakGrew += _ => growCount++;

            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.AirCapsule, DrillDirection.Down);
            streak.NotifyDrill(CellType.Hard, DrillDirection.Down);

            Assert.AreEqual(1, growCount, "only the ColorA drill should have fired StreakGrew");
        }

        // ── Reset ────────────────────────────────────────────────────────────

        [Test]
        public void Reset_ZeroesStreakAndColor()
        {
            var streak = new StreakTracker();
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);

            streak.Reset();

            Assert.AreEqual(0, streak.CurrentStreak);
            Assert.AreEqual(CellType.Empty, streak.CurrentColor);
        }

        [Test]
        public void Reset_ThenNewColor_StartsCleanStreak_NoSpuriousBrokenEvent()
        {
            var streak = new StreakTracker();
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.NotifyDrill(CellType.ColorA, DrillDirection.Down);
            streak.Reset();

            bool broken = false;
            streak.StreakBroken += _ => broken = true;

            streak.NotifyDrill(CellType.ColorB, DrillDirection.Down);

            Assert.IsFalse(broken, "Reset must clear prior state so the next drill starts a fresh streak");
            Assert.AreEqual(1, streak.CurrentStreak);
            Assert.AreEqual(CellType.ColorB, streak.CurrentColor);
        }
    }

    [TestFixture]
    public class ScoreSystemTests
    {
        // ── AwardDrill: 10 × streak ──────────────────────────────────────────

        [Test]
        public void AwardDrill_StreakOne_ScoresTen()
        {
            var score = new ScoreSystem();
            score.AwardDrill(1);
            Assert.AreEqual(10, score.Score);
        }

        [Test]
        public void AwardDrill_StreakFive_ScoresFifty()
        {
            var score = new ScoreSystem();
            score.AwardDrill(5);
            Assert.AreEqual(50, score.Score);
        }

        // ── AwardBurst: cells × 25 × floor(fall / 2) ─────────────────────────

        [Test]
        public void AwardBurst_SixCellsFallThree_Scores150()
        {
            var score = new ScoreSystem();
            score.AwardBurst(cellCount: 6, fallDistance: 3);
            Assert.AreEqual(150, score.Score, "6 × 25 × floor(3/2)=1");
        }

        [Test]
        public void AwardBurst_SixCellsFallFour_Scores300()
        {
            var score = new ScoreSystem();
            score.AwardBurst(cellCount: 6, fallDistance: 4);
            Assert.AreEqual(300, score.Score, "6 × 25 × floor(4/2)=2");
        }

        // ── AwardBomb: blocks × 25 × chain ───────────────────────────────────

        [Test]
        public void AwardBomb_SevenBlocksChainOne_Scores175()
        {
            var score = new ScoreSystem();
            score.AwardBomb(blocksDestroyed: 7, chainMult: 1);
            Assert.AreEqual(175, score.Score);
        }

        [Test]
        public void AwardBomb_FiveBlocksChainThree_Scores375()
        {
            var score = new ScoreSystem();
            score.AwardBomb(blocksDestroyed: 5, chainMult: 3);
            Assert.AreEqual(375, score.Score);
        }

        // ── AwardDepth / AwardPerfectClear ───────────────────────────────────

        [Test]
        public void AwardDepth_ScoresFifty()
        {
            var score = new ScoreSystem();
            score.AwardDepth(7);
            Assert.AreEqual(50, score.Score);
        }

        [Test]
        public void AwardDepth_TracksTheRowIndex_NotTheAwardCount()
        {
            var score = new ScoreSystem();
            score.AwardDepth(11);
            score.AwardDepth(12);
            score.AwardDepth(13);

            Assert.AreEqual(13, score.MaxDepth, "MaxDepth is a row index, not a tally of awards");
            Assert.AreEqual(150, score.Score, "still 50 per new row");
        }

        [Test]
        public void AwardDepth_ShallowerRow_DoesNotEraseTheRecord()
        {
            // ScoreSystem spans the whole run; DepthTracker resets per board, so level 2 starts
            // reporting shallow rows again. The run record must survive that.
            var score = new ScoreSystem();
            score.AwardDepth(20);
            score.AwardDepth(2);

            Assert.AreEqual(20, score.MaxDepth);
            Assert.AreEqual(100, score.Score, "the award still pays, only the record is kept");
        }

        [Test]
        public void AwardDepth_NegativeRow_ClampsToZero()
        {
            var score = new ScoreSystem();
            score.AwardDepth(-5);
            Assert.AreEqual(0, score.MaxDepth);
        }

        [Test]
        public void AwardPerfectClear_CascadeOne_Scores500()
        {
            var score = new ScoreSystem();
            score.AwardPerfectClear(1);
            Assert.AreEqual(500, score.Score);
        }

        [Test]
        public void AwardPerfectClear_IsFlat_CascadeDoesNotScaleThePoints()
        {
            // §15.2: cascades averaged ×4–×7 in play and Perfect Clear took 79–91% of the score.
            // Design rule 7 says flat "+500", and rule 7 wins.
            var score = new ScoreSystem();
            score.AwardPerfectClear(2);
            Assert.AreEqual(500, score.Score);

            score.AwardPerfectClear(7);
            Assert.AreEqual(1000, score.Score, "a ×7 cascade is still worth one flat jackpot");
        }

        [Test]
        public void AwardPerfectClear_StillReportsCascadeInDetail()
        {
            // The HUD popup and chain SFX still read the cascade step even though it costs nothing.
            var score = new ScoreSystem();
            ScoreEvent? evt = null;
            score.OnScore += e => evt = e;

            score.AwardPerfectClear(3);

            Assert.AreEqual(3, evt.Value.Detail);
        }

        // ── AwardDiamond (R4, §6.4) ────────────────────────────────────────

        [Test]
        public void AwardDiamond_Scores150()
        {
            var score = new ScoreSystem();
            score.AwardDiamond();
            Assert.AreEqual(150, score.Score);
        }

        [Test]
        public void AwardDiamond_IsFlat_PerCollection()
        {
            var score = new ScoreSystem();
            score.AwardDiamond();
            score.AwardDiamond();
            score.AwardDiamond();
            Assert.AreEqual(450, score.Score, "every diamond pays the same, however it was collected");
        }

        [Test]
        public void AwardDiamond_FiresOnScore_WithDiamondSource()
        {
            var score = new ScoreSystem();
            ScoreEvent? evt = null;
            score.OnScore += e => evt = e;

            score.AwardDiamond();

            Assert.AreEqual(150, evt.Value.Points);
            Assert.AreEqual(ScoreSource.Diamond, evt.Value.Source);
        }

        // ── AwardEnemyKill / AwardBoomerBlast (§6.5, R5.11) ──────────────────

        [Test]
        public void AwardEnemyKill_Crawler_BonusOne_Scores100()
        {
            var score = new ScoreSystem();
            score.AwardEnemyKill(EnemyType.Crawler, 1);
            Assert.AreEqual(100, score.Score);
        }

        [Test]
        public void AwardEnemyKill_Crawler_BonusThree_Scores300()
        {
            // e.g. a Crawler killed by a chunk burst with fall_bonus = 3.
            var score = new ScoreSystem();
            score.AwardEnemyKill(EnemyType.Crawler, 3);
            Assert.AreEqual(300, score.Score, "100 × fall_bonus 3");
        }

        [Test]
        public void AwardEnemyKill_Boomer_BonusOne_Scores150()
        {
            var score = new ScoreSystem();
            score.AwardEnemyKill(EnemyType.Boomer, 1);
            Assert.AreEqual(150, score.Score);
        }

        [Test]
        public void AwardEnemyKill_DefaultBonus_IsOne()
        {
            // A plain crush kill has no fall_bonus/chain_mult — the caller can omit the bonus.
            var score = new ScoreSystem();
            score.AwardEnemyKill(EnemyType.Crawler);
            Assert.AreEqual(100, score.Score);
        }

        [Test]
        public void AwardEnemyKill_BonusBelowOne_ClampsToOne()
        {
            var score = new ScoreSystem();
            score.AwardEnemyKill(EnemyType.Boomer, 0);
            Assert.AreEqual(150, score.Score);
        }

        [Test]
        public void AwardEnemyKill_FiresOnScore_WithEnemyKillSource()
        {
            var score = new ScoreSystem();
            ScoreEvent? evt = null;
            score.OnScore += e => evt = e;

            score.AwardEnemyKill(EnemyType.Crawler, 2);

            Assert.AreEqual(200, evt.Value.Points);
            Assert.AreEqual(ScoreSource.EnemyKill, evt.Value.Source);
            Assert.AreEqual(2, evt.Value.Detail, "Detail carries the bonus that scaled the kill");
        }

        [Test]
        public void AwardBoomerBlast_FourBlocksBonusTwo_Scores200()
        {
            var score = new ScoreSystem();
            score.AwardBoomerBlast(blocksDestroyed: 4, parentBonus: 2);
            Assert.AreEqual(200, score.Score, "4 × 25 × 2");
        }

        [Test]
        public void AwardBoomerBlast_ParentBonusBelowOne_ClampsToOne()
        {
            var score = new ScoreSystem();
            score.AwardBoomerBlast(blocksDestroyed: 4, parentBonus: 0);
            Assert.AreEqual(100, score.Score, "4 × 25 × 1 (clamped)");
        }

        [Test]
        public void AwardBoomerBlast_NegativeBlocks_ClampsToZero()
        {
            var score = new ScoreSystem();
            score.AwardBoomerBlast(blocksDestroyed: -3, parentBonus: 2);
            Assert.AreEqual(0, score.Score);
        }

        [Test]
        public void AwardBoomerBlast_FiresOnScore_WithBoomerBlastSource()
        {
            var score = new ScoreSystem();
            ScoreEvent? evt = null;
            score.OnScore += e => evt = e;

            score.AwardBoomerBlast(blocksDestroyed: 4, parentBonus: 2);

            Assert.AreEqual(200, evt.Value.Points);
            Assert.AreEqual(ScoreSource.BoomerBlast, evt.Value.Source);
            Assert.AreEqual(2, evt.Value.Detail, "Detail carries the parent bonus, not the block count");
        }

        // ── Accumulation and tracked bests ───────────────────────────────────

        [Test]
        public void Score_AccumulatesAcrossEverySource()
        {
            var score = new ScoreSystem();
            score.AwardDrill(3);                      //  30
            score.AwardBurst(4, 4);                   // 200
            score.AwardBomb(2, 2);                    // 100
            score.AwardDepth(9);                      //  50
            score.AwardPerfectClear(1);               // 500
            score.AwardDiamond();                     // 150
            score.AwardEnemyKill(EnemyType.Crawler, 2); // 200
            score.AwardBoomerBlast(4, 2);              // 200

            Assert.AreEqual(1430, score.Score);
        }

        [Test]
        public void BestStreak_TracksTheMaximum_NotTheLatest()
        {
            var score = new ScoreSystem();
            score.AwardDrill(2);
            score.AwardDrill(9);
            score.AwardDrill(4);

            Assert.AreEqual(9, score.BestStreak);
        }

        [Test]
        public void BestStreakColor_RecordsTheColorOfTheBestStreak_NotTheLatest()
        {
            var score = new ScoreSystem();
            score.AwardDrill(3, CellType.ColorA);
            score.AwardDrill(9, CellType.ColorB); // the best one
            score.AwardDrill(4, CellType.ColorC);

            Assert.AreEqual(9, score.BestStreak);
            Assert.AreEqual(CellType.ColorB, score.BestStreakColor,
                "the run summary reports the color the best streak ran on");
        }

        [Test]
        public void BiggestBurstFallBonus_PairsWithTheRecordedBurst()
        {
            var score = new ScoreSystem();
            score.AwardBurst(9, 6); // 9 cells, ×3 fall bonus — the biggest
            score.AwardBurst(3, 8); // higher bonus but a smaller chunk

            Assert.AreEqual(9, score.BiggestBurst);
            Assert.AreEqual(3, score.BiggestBurstFallBonus,
                "the bonus must describe the burst that was recorded, not a later one");
        }

        [Test]
        public void BiggestBurst_AndBestBombChain_TrackMaximums()
        {
            var score = new ScoreSystem();
            score.AwardBurst(9, 2);
            score.AwardBurst(3, 2);
            score.AwardBomb(1, 5);
            score.AwardBomb(1, 2);

            Assert.AreEqual(9, score.BiggestBurst);
            Assert.AreEqual(5, score.BestBombChain);
        }

        [Test]
        public void PerfectClears_CountsEveryAward()
        {
            var score = new ScoreSystem();
            score.AwardPerfectClear(1);
            score.AwardPerfectClear(2);

            Assert.AreEqual(2, score.PerfectClears);
        }

        // ── OnScore event ────────────────────────────────────────────────────

        [Test]
        public void OnScore_FiresWithCorrectEvent_ForEverySource()
        {
            var score  = new ScoreSystem();
            var events = new List<ScoreEvent>();
            score.OnScore += events.Add;

            score.AwardDrill(5);
            score.AwardBurst(6, 4);
            score.AwardBomb(5, 3);
            score.AwardDepth(42);
            score.AwardPerfectClear(2);

            Assert.AreEqual(5, events.Count);

            Assert.AreEqual(50, events[0].Points);
            Assert.AreEqual(ScoreSource.Streak, events[0].Source);
            Assert.AreEqual(5, events[0].Detail, "detail = streak step");

            Assert.AreEqual(300, events[1].Points);
            Assert.AreEqual(ScoreSource.Burst, events[1].Source);
            Assert.AreEqual(6, events[1].Detail, "detail = cell count");

            Assert.AreEqual(375, events[2].Points);
            Assert.AreEqual(ScoreSource.Bomb, events[2].Source);
            Assert.AreEqual(3, events[2].Detail, "detail = chain multiplier");

            Assert.AreEqual(50, events[3].Points);
            Assert.AreEqual(ScoreSource.Depth, events[3].Source);
            Assert.AreEqual(42, events[3].Detail, "detail = depth row");

            Assert.AreEqual(500, events[4].Points, "flat — cascade no longer scales it");
            Assert.AreEqual(ScoreSource.PerfectClear, events[4].Source);
            Assert.AreEqual(2, events[4].Detail, "detail = cascade step");
        }

        // ── Defensive clamping (§12: clamp, never throw) ─────────────────────

        [Test]
        public void AwardDrill_StreakBelowOne_ClampsToOne()
        {
            var score = new ScoreSystem();
            score.AwardDrill(0);
            Assert.AreEqual(10, score.Score, "a drill always pays at least once");
        }

        [Test]
        public void AwardBurst_BelowThreshold_ScoresNothing()
        {
            var score = new ScoreSystem();
            score.AwardBurst(cellCount: 6, fallDistance: 1);
            Assert.AreEqual(0, score.Score, "floor(1/2) = 0 — a 1-row drop never bursts");
        }

        [Test]
        public void Reset_ZeroesEverything()
        {
            var score = new ScoreSystem();
            score.AwardDrill(7);
            score.AwardBurst(9, 6);
            score.AwardBomb(3, 4);
            score.AwardDepth(18);
            score.AwardPerfectClear(2);

            score.Reset();

            Assert.AreEqual(0, score.Score);
            Assert.AreEqual(0, score.BestStreak);
            Assert.AreEqual(CellType.Empty, score.BestStreakColor);
            Assert.AreEqual(0, score.BiggestBurst);
            Assert.AreEqual(0, score.BiggestBurstFallBonus);
            Assert.AreEqual(0, score.BestBombChain);
            Assert.AreEqual(0, score.PerfectClears);
            Assert.AreEqual(0, score.MaxDepth);
        }
    }

    [TestFixture]
    public class DepthTrackerTests
    {
        [Test]
        public void DeeperPosition_UpdatesMaxDepth_AndFiresEvent()
        {
            var depth = new DepthTracker();
            int? fired = null;
            depth.NewDepthReached += row => fired = row;

            depth.NotifyPosition(new GridPos(3, 5));

            Assert.AreEqual(5, depth.MaxDepth);
            Assert.AreEqual(5, fired);
        }

        [Test]
        public void SameDepth_DoesNotFireEvent()
        {
            var depth = new DepthTracker();
            depth.NotifyPosition(new GridPos(3, 5));

            bool fired = false;
            depth.NewDepthReached += _ => fired = true;
            depth.NotifyPosition(new GridPos(4, 5));

            Assert.IsFalse(fired, "revisiting the same row must not fire NewDepthReached");
            Assert.AreEqual(5, depth.MaxDepth);
        }

        [Test]
        public void ShallowerPosition_DoesNotFireEvent_AndDoesNotLowerMaxDepth()
        {
            var depth = new DepthTracker();
            depth.NotifyPosition(new GridPos(0, 10));

            bool fired = false;
            depth.NewDepthReached += _ => fired = true;
            depth.NotifyPosition(new GridPos(0, 4));

            Assert.IsFalse(fired, "climbing back up must not fire NewDepthReached");
            Assert.AreEqual(10, depth.MaxDepth, "MaxDepth must never decrease");
        }

        [Test]
        public void Reset_ZeroesMaxDepth()
        {
            var depth = new DepthTracker();
            depth.NotifyPosition(new GridPos(0, 12));

            depth.Reset();

            Assert.AreEqual(0, depth.MaxDepth);
        }

        [Test]
        public void SuccessiveDescents_FireEventOnEveryNewRecord()
        {
            var depth = new DepthTracker();
            var records = new List<int>();
            depth.NewDepthReached += records.Add;

            depth.NotifyPosition(new GridPos(0, 1));
            depth.NotifyPosition(new GridPos(0, 1)); // no new record
            depth.NotifyPosition(new GridPos(0, 2));
            depth.NotifyPosition(new GridPos(0, 0)); // shallower, no new record
            depth.NotifyPosition(new GridPos(0, 5));

            CollectionAssert.AreEqual(new[] { 1, 2, 5 }, records);
            Assert.AreEqual(5, depth.MaxDepth);
        }
    }

    [TestFixture]
    public class DiamondSystemTests
    {
        [Test]
        public void Init_SetsTotal_AndZeroesCollected()
        {
            var diamonds = new DiamondSystem();
            diamonds.Init(3);

            Assert.AreEqual(0, diamonds.Collected);
            Assert.AreEqual(3, diamonds.Total);
        }

        [Test]
        public void NotifyCollected_IncrementsCollected_AndFiresEvent()
        {
            var diamonds = new DiamondSystem();
            diamonds.Init(3);
            (int collected, int total)? fired = null;
            diamonds.DiamondCollected += (c, t) => fired = (c, t);

            diamonds.NotifyCollected(new GridPos(1, 1));

            Assert.AreEqual(1, diamonds.Collected);
            Assert.AreEqual((1, 3), fired);
        }

        [Test]
        public void IsComplete_FalseUntilAllCollected()
        {
            var diamonds = new DiamondSystem();
            diamonds.Init(2);

            diamonds.NotifyCollected(new GridPos(0, 0));
            Assert.IsFalse(diamonds.IsComplete);

            diamonds.NotifyCollected(new GridPos(1, 0));
            Assert.IsTrue(diamonds.IsComplete);
        }

        [Test]
        public void AllDiamondsCollected_FiresOnlyOnTheLastOne()
        {
            var diamonds = new DiamondSystem();
            diamonds.Init(2);
            int fireCount = 0;
            diamonds.AllDiamondsCollected += () => fireCount++;

            diamonds.NotifyCollected(new GridPos(0, 0));
            Assert.AreEqual(0, fireCount, "must not fire before every diamond is collected");

            diamonds.NotifyCollected(new GridPos(1, 0));
            Assert.AreEqual(1, fireCount, "must fire exactly once when the last diamond is collected");
        }

        [Test]
        public void ZeroDiamonds_IsCompleteImmediately()
        {
            var diamonds = new DiamondSystem();
            diamonds.Init(0);

            Assert.IsTrue(diamonds.IsComplete, "a level with no diamonds must leave the gate open");
        }

        [Test]
        public void Reset_ZeroesCollectedAndTotal()
        {
            var diamonds = new DiamondSystem();
            diamonds.Init(3);
            diamonds.NotifyCollected(new GridPos(0, 0));

            diamonds.Reset();

            Assert.AreEqual(0, diamonds.Collected);
            Assert.AreEqual(0, diamonds.Total);
        }

        [Test]
        public void Init_OnNewBoard_ResetsPreviousCollection()
        {
            var diamonds = new DiamondSystem();
            diamonds.Init(2);
            diamonds.NotifyCollected(new GridPos(0, 0));
            diamonds.NotifyCollected(new GridPos(1, 0));
            Assert.IsTrue(diamonds.IsComplete);

            diamonds.Init(4); // next level loads with more diamonds

            Assert.AreEqual(0, diamonds.Collected);
            Assert.AreEqual(4, diamonds.Total);
            Assert.IsFalse(diamonds.IsComplete);
        }
    }

    [TestFixture]
    public class EndlessManagerTests
    {
        // Board layout for the default segment: SpawnRows (3) + SegmentRows + FloorRows (1).
        private static int BoardHeight(EndlessManager m) =>
            StrateGenerator.SpawnRows + m.SegmentRows + StrateGenerator.FloorRows;

        // ── Air drain ramp (GDD §7: 5.0 + depth/20 × 0.5, capped at 10) ───────

        [Test]
        public void DrainRateForDepth_FollowsTheGddCurve()
        {
            Assert.AreEqual(5.0f, EndlessManager.DrainRateForDepth(0),   0.0001f);
            Assert.AreEqual(5.5f, EndlessManager.DrainRateForDepth(20),  0.0001f);
            Assert.AreEqual(7.5f, EndlessManager.DrainRateForDepth(100), 0.0001f);
        }

        [Test]
        public void DrainRateForDepth_IsCappedAtMax()
        {
            Assert.AreEqual(EndlessManager.MaxDrainRate, EndlessManager.DrainRateForDepth(200),   0.0001f);
            Assert.AreEqual(EndlessManager.MaxDrainRate, EndlessManager.DrainRateForDepth(10000), 0.0001f);
        }

        [Test]
        public void DrainRateForDepth_NegativeDepth_ClampsToBase()
        {
            Assert.AreEqual(EndlessManager.BaseDrainRate, EndlessManager.DrainRateForDepth(-5), 0.0001f);
        }

        [Test]
        public void DescendingRaisesTheDrainRate_AndFiresTheEvent()
        {
            var endless = new EndlessManager(seed: 1);
            float? fired = null;
            endless.DrainRateChanged += r => fired = r;

            Assert.AreEqual(EndlessManager.BaseDrainRate, endless.DrainRate, 0.0001f);

            endless.NotifyAvatarPosition(new GridPos(3, StrateGenerator.SpawnRows + 40), BoardHeight(endless));

            Assert.AreEqual(EndlessManager.DrainRateForDepth(40), endless.DrainRate, 0.0001f);
            Assert.IsNotNull(fired);
            Assert.Greater(endless.DrainRate, EndlessManager.BaseDrainRate);
        }

        // ── Depth accounting ─────────────────────────────────────────────────

        [Test]
        public void SpawnZone_IsDepthZero()
        {
            var endless = new EndlessManager(seed: 1);
            bool fired = false;
            endless.DepthChanged += _ => fired = true;

            for (int y = 0; y < StrateGenerator.SpawnRows; y++)
                endless.NotifyAvatarPosition(new GridPos(3, y), BoardHeight(endless));

            Assert.AreEqual(0, endless.Depth, "hovering in the spawn zone is not progress");
            Assert.IsFalse(fired);
        }

        [Test]
        public void Descending_FiresDepthChanged_OnEveryNewRecordOnly()
        {
            var endless = new EndlessManager(seed: 1);
            var records = new List<int>();
            endless.DepthChanged += records.Add;

            int h = BoardHeight(endless);
            endless.NotifyAvatarPosition(new GridPos(3, StrateGenerator.SpawnRows + 1), h); // depth 1
            endless.NotifyAvatarPosition(new GridPos(4, StrateGenerator.SpawnRows + 1), h); // same row
            endless.NotifyAvatarPosition(new GridPos(4, StrateGenerator.SpawnRows + 3), h); // depth 3
            endless.NotifyAvatarPosition(new GridPos(4, StrateGenerator.SpawnRows),     h); // climbed back

            CollectionAssert.AreEqual(new[] { 1, 3 }, records);
            Assert.AreEqual(3, endless.Depth, "Depth must never decrease");
        }

        [Test]
        public void Depth_IsContinuousAcrossASegmentBoundary()
        {
            var endless = new EndlessManager(seed: 1);
            int h = BoardHeight(endless);

            // Bottom of segment 0 = the last content row.
            endless.NotifyAvatarPosition(new GridPos(3, h - EndlessManager.ExitDepthFromFloor), h);
            int depthAtBottom = endless.Depth;
            Assert.AreEqual(endless.SegmentRows - 1, depthAtBottom);

            endless.AdvanceSegment();
            Assert.AreEqual(endless.SegmentRows, endless.SegmentStartDepth);

            // First content row of segment 1 continues exactly where segment 0 stopped: no
            // jump, no repeat.
            endless.NotifyAvatarPosition(new GridPos(3, StrateGenerator.SpawnRows), h);
            Assert.AreEqual(depthAtBottom + 1, endless.Depth);
        }

        [Test]
        public void SpawningIntoANewSegment_DoesNotLoseDepth()
        {
            var endless = new EndlessManager(seed: 1);
            int h = BoardHeight(endless);
            endless.NotifyAvatarPosition(new GridPos(3, h - EndlessManager.ExitDepthFromFloor), h);
            endless.AdvanceSegment();

            // Avatar respawns at the top of the new board — its LOCAL row is shallow again.
            endless.NotifyAvatarPosition(new GridPos(3, 0), h);

            Assert.AreEqual(endless.SegmentRows - 1, endless.Depth,
                "the run's depth must survive the board swap");
        }

        // ── Segment progression ──────────────────────────────────────────────

        [Test]
        public void SegmentExhausted_FiresOnceWhenTheAvatarReachesTheBottom()
        {
            var endless = new EndlessManager(seed: 1);
            int h = BoardHeight(endless);
            var fired = new List<int>();
            endless.SegmentExhausted += fired.Add;

            endless.NotifyAvatarPosition(new GridPos(3, h - EndlessManager.ExitDepthFromFloor - 1), h);
            Assert.AreEqual(0, fired.Count, "one row short of the bottom is not the bottom");

            endless.NotifyAvatarPosition(new GridPos(3, h - EndlessManager.ExitDepthFromFloor), h);
            endless.NotifyAvatarPosition(new GridPos(4, h - EndlessManager.ExitDepthFromFloor), h);

            CollectionAssert.AreEqual(new[] { 0 }, fired, "must fire exactly once per segment");
        }

        [Test]
        public void AdvanceSegment_ReArmsTheExhaustedGuard()
        {
            var endless = new EndlessManager(seed: 1);
            int h = BoardHeight(endless);
            var fired = new List<int>();
            endless.SegmentExhausted += fired.Add;

            endless.NotifyAvatarPosition(new GridPos(3, h - EndlessManager.ExitDepthFromFloor), h);
            endless.AdvanceSegment();
            endless.NotifyAvatarPosition(new GridPos(3, h - EndlessManager.ExitDepthFromFloor), h);

            CollectionAssert.AreEqual(new[] { 0, 1 }, fired);
            Assert.AreEqual(1, endless.SegmentIndex);
        }

        // ── Board generation ─────────────────────────────────────────────────

        [Test]
        public void BuildCurrentSegment_IsDeterministicForTheSameSeed()
        {
            string[] a = new EndlessManager(seed: 4242).BuildCurrentSegment();
            string[] b = new EndlessManager(seed: 4242).BuildCurrentSegment();

            CollectionAssert.AreEqual(a, b);
            Assert.AreEqual(StrateGenerator.SpawnRows + EndlessManager.DefaultSegmentRows + StrateGenerator.FloorRows,
                            a.Length);
        }

        [Test]
        public void ConsecutiveSegments_ProduceDifferentBoards()
        {
            var endless = new EndlessManager(seed: 4242);
            string[] first = endless.BuildCurrentSegment();
            endless.AdvanceSegment();
            string[] second = endless.BuildCurrentSegment();

            CollectionAssert.AreNotEqual(first, second, "a run must not loop the same well over and over");
        }

        [Test]
        public void SegmentSeed_IsStable_AndDistinctPerIndex()
        {
            Assert.AreEqual(EndlessManager.SegmentSeed(9, 3), EndlessManager.SegmentSeed(9, 3));
            Assert.AreNotEqual(EndlessManager.SegmentSeed(9, 3), EndlessManager.SegmentSeed(9, 4));
            Assert.AreNotEqual(EndlessManager.SegmentSeed(9, 3), EndlessManager.SegmentSeed(10, 3));
        }

        [Test]
        public void Reset_StartsAFreshRunOnTheNewSeed()
        {
            var endless = new EndlessManager(seed: 1);
            int h = BoardHeight(endless);
            endless.NotifyAvatarPosition(new GridPos(3, h - EndlessManager.ExitDepthFromFloor), h);
            endless.AdvanceSegment();

            endless.Reset(seed: 99);

            Assert.AreEqual(99, endless.Seed);
            Assert.AreEqual(0,  endless.SegmentIndex);
            Assert.AreEqual(0,  endless.SegmentStartDepth);
            Assert.AreEqual(0,  endless.Depth);
            Assert.AreEqual(EndlessManager.BaseDrainRate, endless.DrainRate, 0.0001f);
        }
    }

    [TestFixture]
    public class DailyDigTests
    {
        [Test]
        public void SameDay_AlwaysProducesTheSameSeed()
        {
            var morning = new System.DateTime(2026, 7, 26, 06, 30, 00, System.DateTimeKind.Utc);
            var night   = new System.DateTime(2026, 7, 26, 23, 59, 59, System.DateTimeKind.Utc);

            Assert.AreEqual(DailyDig.SeedFor(morning), DailyDig.SeedFor(night),
                "the daily well must not change with the time of day");
        }

        [Test]
        public void DifferentDays_ProduceDifferentSeeds()
        {
            var day1 = new System.DateTime(2026, 7, 26, 0, 0, 0, System.DateTimeKind.Utc);
            var day2 = day1.AddDays(1);
            var day3 = day1.AddDays(2);

            Assert.AreNotEqual(DailyDig.SeedFor(day1), DailyDig.SeedFor(day2));
            Assert.AreNotEqual(DailyDig.SeedFor(day2), DailyDig.SeedFor(day3));
        }

        [Test]
        public void ConsecutiveDays_ProduceUnrelatedBoards()
        {
            // Raw consecutive integers fed to System.Random give visibly related boards, which
            // would make "today's dig" feel like a re-skin of yesterday's — hence the SplitMix
            // finalizer. Compare the actual generated terrain, not just the seeds.
            var day = new System.DateTime(2026, 7, 26, 0, 0, 0, System.DateTimeKind.Utc);

            string[] a = StrateGenerator.EndlessSegment(DailyDig.SeedFor(day), StrateGenerator.DefaultWidth, rows: 40, startDepth: 0);
            string[] b = StrateGenerator.EndlessSegment(DailyDig.SeedFor(day.AddDays(1)), StrateGenerator.DefaultWidth, rows: 40, startDepth: 0);

            int identicalRows = 0;
            for (int y = 0; y < a.Length; y++)
                if (a[y] == b[y]) identicalRows++;

            // Spawn rows and the bedrock floor are identical by construction; the content must not be.
            int structural = StrateGenerator.SpawnRows + StrateGenerator.FloorRows;
            Assert.LessOrEqual(identicalRows, structural + 2,
                $"{identicalRows} of {a.Length} rows match — consecutive days are generating near-identical wells");
        }

        [Test]
        public void SeedIsStableAcrossRuns_ForAKnownDate()
        {
            // Pins the mapping itself: every player, every platform, every launch must derive the
            // same seed for a given day, or the leaderboard compares runs on different terrain.
            var day = new System.DateTime(2026, 7, 26, 0, 0, 0, System.DateTimeKind.Utc);
            int expected = DailyDig.SeedFor(day);

            for (int i = 0; i < 5; i++)
                Assert.AreEqual(expected, DailyDig.SeedFor(new System.DateTime(2026, 7, 26, i * 4, 0, 0, System.DateTimeKind.Utc)));
        }

        [Test]
        public void SeedIsNeverIntMinValue()
        {
            // int.MinValue has no positive counterpart — some System.Random implementations throw.
            var day = new System.DateTime(2020, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
            for (int i = 0; i < 4000; i++)
                Assert.AreNotEqual(int.MinValue, DailyDig.SeedFor(day.AddDays(i)));
        }

        [Test]
        public void Label_IsInvariantAndSortable()
        {
            var day = new System.DateTime(2026, 7, 6, 14, 22, 00, System.DateTimeKind.Utc);
            Assert.AreEqual("2026-07-06", DailyDig.LabelFor(day), "the label is a stable key, never localized");
        }
    }

    [TestFixture]
    public class EnemySystemTests
    {
        // ── Spawn / activate ────────────────────────────────────────────────

        [Test]
        public void SpawnEnemy_CreatesDormantEnemy_AndFiresEvent()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var pos = new GridPos(2, 3);

            (int id, EnemyType type, GridPos at)? reported = null;
            enemies.EnemySpawned += (id, type, p) => reported = (id, type, p);

            int spawnedId = enemies.SpawnEnemy(EnemyType.Crawler, pos);

            Assert.IsNotNull(reported);
            Assert.AreEqual(spawnedId, reported.Value.id);
            Assert.AreEqual(EnemyType.Crawler, reported.Value.type);
            Assert.AreEqual(pos, reported.Value.at);

            EnemyEntity? enemy = enemies.GetEnemyAt(pos);
            Assert.IsTrue(enemy.HasValue);
            Assert.IsFalse(enemy.Value.IsActive, "a freshly spawned enemy must be dormant");
            Assert.IsTrue(enemy.Value.IsAlive);
            Assert.AreEqual(1, enemy.Value.Health);
        }

        [Test]
        public void ActivateEnemy_MakesItActive_AndFiresEvent()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            int id = enemies.SpawnEnemy(EnemyType.Crawler, new GridPos(0, 0));

            int? reportedId = null;
            enemies.EnemyActivated += activatedId => reportedId = activatedId;

            enemies.ActivateEnemy(id);

            Assert.AreEqual(id, reportedId);
            Assert.IsTrue(enemies.GetEnemyAt(new GridPos(0, 0)).Value.IsActive);
        }

        [Test]
        public void ActivateEnemy_UnknownId_DoesNotThrow_NoEvent()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            bool fired = false;
            enemies.EnemyActivated += _ => fired = true;

            Assert.DoesNotThrow(() => enemies.ActivateEnemy(999));
            Assert.IsFalse(fired);
        }

        // ── Activation triggers (§6.5, R5.9) ─────────────────────────────────

        [Test]
        public void NotifyAdjacentDrill_WakesDormantCrawlerNextDoor()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var crawlerPos = new GridPos(1, 0);
            int id = enemies.SpawnEnemy(EnemyType.Crawler, crawlerPos);

            int? activatedId = null;
            enemies.EnemyActivated += activated => activatedId = activated;

            enemies.NotifyAdjacentDrill(new GridPos(0, 0)); // right neighbor is (1,0) — the Crawler

            Assert.AreEqual(id, activatedId);
            Assert.IsTrue(enemies.GetEnemyAt(crawlerPos).Value.IsActive);
        }

        [Test]
        public void NotifyAdjacentDrill_TwoCellsAway_DoesNotActivate()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var crawlerPos = new GridPos(2, 0);
            enemies.SpawnEnemy(EnemyType.Crawler, crawlerPos);

            bool fired = false;
            enemies.EnemyActivated += _ => fired = true;

            enemies.NotifyAdjacentDrill(new GridPos(0, 0)); // 2 cells from the Crawler — not adjacent

            Assert.IsFalse(fired);
            Assert.IsFalse(enemies.GetEnemyAt(crawlerPos).Value.IsActive);
        }

        [Test]
        public void NotifyBurst_ShockwaveAdjacentToDormantEnemy_ActivatesIt()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var crawlerPos = new GridPos(5, 0);
            enemies.SpawnEnemy(EnemyType.Crawler, crawlerPos);

            // The Crawler sits just outside the effect zone — adjacent to a shockwave cell, not
            // inside it — so it wakes up without being killed by the same burst.
            enemies.NotifyBurst(new List<GridPos> { new GridPos(0, 0) }, new List<GridPos> { new GridPos(4, 0) }, fallDist: 2);

            Assert.IsTrue(enemies.GetEnemyAt(crawlerPos).Value.IsActive, "a shockwave cell adjacent to a dormant enemy must wake it");
            Assert.IsTrue(enemies.GetEnemyAt(crawlerPos).HasValue, "and it must survive — it was adjacent, not inside the zone");
        }

        [Test]
        public void NotifyBombBlast_AdjacentToDormantEnemy_ActivatesIt()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var crawlerPos = new GridPos(5, 0);
            enemies.SpawnEnemy(EnemyType.Crawler, crawlerPos);

            enemies.NotifyBombBlast(new List<GridPos> { new GridPos(4, 0) }, chainMult: 1);

            Assert.IsTrue(enemies.GetEnemyAt(crawlerPos).Value.IsActive, "a blast cell adjacent to a dormant enemy must wake it");
        }

        [Test]
        public void ActivateInViewport_WakesEnemiesInsideTheRange()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            int inRangeTop    = enemies.SpawnEnemy(EnemyType.Crawler, new GridPos(0, 5));
            int inRangeBottom = enemies.SpawnEnemy(EnemyType.Crawler, new GridPos(0, 8));

            enemies.ActivateInViewport(5, 8);

            Assert.IsTrue(enemies.GetEnemyAt(new GridPos(0, 5)).Value.IsActive, "the top edge of the range must activate");
            Assert.IsTrue(enemies.GetEnemyAt(new GridPos(0, 8)).Value.IsActive, "the bottom edge of the range must activate");
        }

        [Test]
        public void ActivateInViewport_DoesNotWakeEnemiesOutsideTheRange()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            enemies.SpawnEnemy(EnemyType.Crawler, new GridPos(0, 2)); // above the range
            enemies.SpawnEnemy(EnemyType.Crawler, new GridPos(0, 9)); // below the range

            enemies.ActivateInViewport(5, 8);

            Assert.IsFalse(enemies.GetEnemyAt(new GridPos(0, 2)).Value.IsActive, "a row above the range must stay dormant");
            Assert.IsFalse(enemies.GetEnemyAt(new GridPos(0, 9)).Value.IsActive, "a row below the range must stay dormant");
        }

        // ── Crawler movement (§6.5, R5.7) ────────────────────────────────────

        [Test]
        public void Crawler_Active_MovesRight_After0_8Seconds()
        {
            var grid = GridModel.FromStringMap(new[] { "......." }); // 7 empty columns, one row
            var enemies = new EnemySystem(grid);
            var start = new GridPos(3, 0);
            int id = enemies.SpawnEnemy(EnemyType.Crawler, start);
            enemies.ActivateEnemy(id);

            enemies.Tick(EnemySystem.CrawlerMoveInterval, new GridPos(0, 0));

            EnemyEntity? moved = enemies.GetEnemyAt(new GridPos(4, 0));
            Assert.IsTrue(moved.HasValue, "the Crawler must step right after one full move interval");
            Assert.AreEqual(id, moved.Value.Id);
            Assert.IsNull(enemies.GetEnemyAt(start), "the old cell must be vacated");
        }

        [Test]
        public void Crawler_BouncesOffRightWall_ThenDirectionIsLeft()
        {
            var grid = GridModel.FromStringMap(new[] { "......." }); // cols 0-6
            var enemies = new EnemySystem(grid);
            var start = new GridPos(6, 0); // rightmost column — the default rightward heading is blocked immediately
            int id = enemies.SpawnEnemy(EnemyType.Crawler, start);
            enemies.ActivateEnemy(id);

            enemies.Tick(EnemySystem.CrawlerMoveInterval, new GridPos(0, 0));

            Assert.IsTrue(enemies.GetEnemyAt(new GridPos(5, 0)).HasValue,
                "hitting the right wall must bounce and step left within the same interval");

            // Confirm the heading actually flipped (not a one-off nudge): it keeps going left.
            enemies.Tick(EnemySystem.CrawlerMoveInterval, new GridPos(0, 0));
            Assert.IsTrue(enemies.GetEnemyAt(new GridPos(4, 0)).HasValue, "direction must stay left after the bounce");
        }

        [Test]
        public void Crawler_BouncesOffSolidBlock_ReversesDirection()
        {
            var grid = GridModel.FromStringMap(new[] { "...H..." }); // Hard block at col 3
            var enemies = new EnemySystem(grid);
            var start = new GridPos(2, 0); // one cell left of the block, default heading is right
            int id = enemies.SpawnEnemy(EnemyType.Crawler, start);
            enemies.ActivateEnemy(id);

            enemies.Tick(EnemySystem.CrawlerMoveInterval, new GridPos(0, 0));

            Assert.IsTrue(enemies.GetEnemyAt(new GridPos(1, 0)).HasValue,
                "a solid block ahead must bounce the Crawler exactly like a wall");
        }

        [Test]
        public void Crawler_Dormant_DoesNotMove_EvenAfter2Seconds()
        {
            var grid = GridModel.FromStringMap(new[] { "......." });
            var enemies = new EnemySystem(grid);
            var start = new GridPos(3, 0);
            enemies.SpawnEnemy(EnemyType.Crawler, start); // never activated

            enemies.Tick(2f, new GridPos(0, 0));

            Assert.IsTrue(enemies.GetEnemyAt(start).HasValue, "a dormant Crawler must never move — Tick ignores it");
        }

        [Test]
        public void Crawler_MakesFullRoundTrip_InAnEmptySevenColumnRow()
        {
            var grid = GridModel.FromStringMap(new[] { "......." }); // cols 0-6
            var enemies = new EnemySystem(grid);
            var start = new GridPos(0, 0);
            int id = enemies.SpawnEnemy(EnemyType.Crawler, start);
            enemies.ActivateEnemy(id);

            // 6 intervals to walk 0→6, a 7th to bounce off the right wall and step back to 5,
            // then 5 more to walk 5→0 — 12 intervals total for a complete round trip.
            for (int i = 0; i < 6; i++)
                enemies.Tick(EnemySystem.CrawlerMoveInterval, new GridPos(0, 0));
            Assert.IsTrue(enemies.GetEnemyAt(new GridPos(6, 0)).HasValue, "must reach the far wall after 6 intervals");

            for (int i = 0; i < 6; i++)
                enemies.Tick(EnemySystem.CrawlerMoveInterval, new GridPos(0, 0));
            Assert.IsTrue(enemies.GetEnemyAt(start).HasValue, "after the full round trip the Crawler must be back at its starting column");
        }

        // ── Avatar contact damage (§6.5, R5.10) ──────────────────────────────

        [Test]
        public void Tick_ActiveCrawlerOnAvatarCell_FiresAvatarHitByEnemy()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var avatarPos = new GridPos(3, 3);
            int id = enemies.SpawnEnemy(EnemyType.Crawler, avatarPos);
            enemies.ActivateEnemy(id);

            int? hitId = null;
            enemies.AvatarHitByEnemy += h => hitId = h;

            enemies.Tick(0.01f, avatarPos); // dt far below the move interval — this is an overlap, not a step

            Assert.AreEqual(id, hitId);
        }

        [Test]
        public void Tick_DormantCrawlerOnAvatarCell_DoesNotFire()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var avatarPos = new GridPos(3, 3);
            enemies.SpawnEnemy(EnemyType.Crawler, avatarPos); // never activated

            bool fired = false;
            enemies.AvatarHitByEnemy += _ => fired = true;

            enemies.Tick(0.01f, avatarPos);

            Assert.IsFalse(fired, "a buried Crawler must never deal contact damage");
        }

        [Test]
        public void Tick_ActiveBoomerOnAvatarCell_DoesNotFire()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var avatarPos = new GridPos(3, 3);
            int id = enemies.SpawnEnemy(EnemyType.Boomer, avatarPos);
            enemies.ActivateEnemy(id);

            bool fired = false;
            enemies.AvatarHitByEnemy += _ => fired = true;

            enemies.Tick(0.01f, avatarPos);

            Assert.IsFalse(fired, "Boomers never deal contact damage, active or not — only their death blast is dangerous, and that's a bonus");
        }

        [Test]
        public void Tick_ActiveCrawlerAtDifferentPosition_DoesNotFire()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            int id = enemies.SpawnEnemy(EnemyType.Crawler, new GridPos(5, 5));
            enemies.ActivateEnemy(id);

            bool fired = false;
            enemies.AvatarHitByEnemy += _ => fired = true;

            enemies.Tick(0.01f, new GridPos(0, 0));

            Assert.IsFalse(fired);
        }

        [Test]
        public void Tick_CrawlerStepsIntoAvatarCell_FiresAvatarHitByEnemy()
        {
            var grid = GridModel.FromStringMap(new[] { "......." });
            var enemies = new EnemySystem(grid);
            var avatarPos = new GridPos(4, 0);
            int id = enemies.SpawnEnemy(EnemyType.Crawler, new GridPos(3, 0));
            enemies.ActivateEnemy(id);

            int? hitId = null;
            enemies.AvatarHitByEnemy += h => hitId = h;

            enemies.Tick(EnemySystem.CrawlerMoveInterval, avatarPos); // steps right into the avatar's cell

            Assert.AreEqual(id, hitId);
            Assert.AreEqual(avatarPos, enemies.GetEnemyAt(avatarPos).Value.Position);
        }

        // ── Queries ──────────────────────────────────────────────────────────

        [Test]
        public void GetEnemyAt_ReturnsTheEnemyAtThatPosition()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var pos = new GridPos(5, 1);
            enemies.SpawnEnemy(EnemyType.Boomer, pos);

            EnemyEntity? found = enemies.GetEnemyAt(pos);

            Assert.IsTrue(found.HasValue);
            Assert.AreEqual(EnemyType.Boomer, found.Value.Type);
            Assert.AreEqual(pos, found.Value.Position);
        }

        [Test]
        public void GetEnemyAt_NoEnemyThere_ReturnsNull()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            enemies.SpawnEnemy(EnemyType.Crawler, new GridPos(0, 0));

            Assert.IsNull(enemies.GetEnemyAt(new GridPos(9, 9)));
        }

        [Test]
        public void GetAllAlive_DeadEnemy_DoesNotReappear()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var pos = new GridPos(1, 1);
            enemies.SpawnEnemy(EnemyType.Crawler, pos);

            enemies.NotifyChunkLanded(new List<GridPos> { pos });

            CollectionAssert.IsEmpty(enemies.GetAllAlive());
            Assert.IsNull(enemies.GetEnemyAt(pos), "a dead enemy must not be found by position either");
        }

        // ── Kill by crush (chunk landing) ───────────────────────────────────

        [Test]
        public void NotifyChunkLanded_KillsEnemyUnderTheChunk_FiresEnemyKilled()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var pos = new GridPos(3, 4);
            int id = enemies.SpawnEnemy(EnemyType.Crawler, pos);

            (int id, EnemyType type, KillMethod method)? killed = null;
            enemies.EnemyKilled += (killedId, type, method, _) => killed = (killedId, type, method);

            enemies.NotifyChunkLanded(new List<GridPos> { new GridPos(0, 0), pos });

            Assert.IsNotNull(killed);
            Assert.AreEqual(id, killed.Value.id);
            Assert.AreEqual(EnemyType.Crawler, killed.Value.type);
            Assert.AreEqual(KillMethod.Crush, killed.Value.method);
            Assert.IsFalse(enemies.GetEnemyAt(pos).HasValue);
        }

        [Test]
        public void NotifyChunkLanded_DormantEnemy_IsStillKilled()
        {
            // Corrected per dev: dormant enemies are buried but physically present — gravity/burst/
            // bomb kill them the same as active ones. Only avatar CONTACT (R5.10) checks IsActive.
            var enemies = new EnemySystem(new GridModel(10, 10));
            var pos = new GridPos(2, 2);
            enemies.SpawnEnemy(EnemyType.Crawler, pos); // never activated — stays dormant

            enemies.NotifyChunkLanded(new List<GridPos> { pos });

            Assert.IsNull(enemies.GetEnemyAt(pos), "a dormant enemy must still die when crushed");
        }

        // ── Kill by burst ────────────────────────────────────────────────────

        [Test]
        public void NotifyBurst_KillsEnemyInBurstZone_FiresEnemyKilled()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var pos = new GridPos(4, 4);
            enemies.SpawnEnemy(EnemyType.Crawler, pos);

            KillMethod? method = null;
            enemies.EnemyKilled += (_, __, m, ___) => method = m;

            enemies.NotifyBurst(new List<GridPos> { pos }, new List<GridPos>(), fallDist: 4);

            Assert.AreEqual(KillMethod.Burst, method);
            Assert.IsFalse(enemies.GetEnemyAt(pos).HasValue);
        }

        [Test]
        public void NotifyBurst_KillsEnemyInShockwaveRing_NotJustBurstCells()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var pos = new GridPos(6, 6);
            enemies.SpawnEnemy(EnemyType.Crawler, pos);

            enemies.NotifyBurst(new List<GridPos> { new GridPos(0, 0) }, new List<GridPos> { pos }, fallDist: 2);

            Assert.IsFalse(enemies.GetEnemyAt(pos).HasValue, "the shockwave ring must kill too, not just the burst footprint");
        }

        [Test]
        public void NotifyBurst_KillingBoomer_FiresBoomerDetonated_WithFallBonus()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var pos = new GridPos(1, 1);
            enemies.SpawnEnemy(EnemyType.Boomer, pos);

            (GridPos pos, int bonus)? detonated = null;
            enemies.BoomerDetonated += (p, bonus, _) => detonated = (p, bonus);

            enemies.NotifyBurst(new List<GridPos> { pos }, new List<GridPos>(), fallDist: 5); // floor(5/2) = 2

            Assert.IsNotNull(detonated);
            Assert.AreEqual(pos, detonated.Value.pos);
            Assert.AreEqual(2, detonated.Value.bonus);
        }

        [Test]
        public void NotifyBurst_KillingCrawler_DoesNotFireBoomerDetonated()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var pos = new GridPos(1, 1);
            enemies.SpawnEnemy(EnemyType.Crawler, pos);

            bool fired = false;
            enemies.BoomerDetonated += (_, __, ___) => fired = true;

            enemies.NotifyBurst(new List<GridPos> { pos }, new List<GridPos>(), fallDist: 5);

            Assert.IsFalse(fired, "only a Boomer kill detonates — a Crawler just dies");
        }

        // ── Kill by bomb blast ───────────────────────────────────────────────

        [Test]
        public void NotifyBombBlast_KillsEnemyInBlastZone_FiresEnemyKilled()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var pos = new GridPos(2, 5);
            enemies.SpawnEnemy(EnemyType.Crawler, pos);

            KillMethod? method = null;
            enemies.EnemyKilled += (_, __, m, ___) => method = m;

            enemies.NotifyBombBlast(new List<GridPos> { pos }, chainMult: 1);

            Assert.AreEqual(KillMethod.Bomb, method);
            Assert.IsFalse(enemies.GetEnemyAt(pos).HasValue);
        }

        [Test]
        public void NotifyBombBlast_KillingBoomer_FiresBoomerDetonated_WithChainMult()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var pos = new GridPos(3, 3);
            enemies.SpawnEnemy(EnemyType.Boomer, pos);

            (GridPos pos, int bonus)? detonated = null;
            enemies.BoomerDetonated += (p, bonus, _) => detonated = (p, bonus);

            enemies.NotifyBombBlast(new List<GridPos> { pos }, chainMult: 3);

            Assert.IsNotNull(detonated);
            Assert.AreEqual(pos, detonated.Value.pos);
            Assert.AreEqual(3, detonated.Value.bonus, "the Boomer's blast must carry the SAME chainMult as the bomb that killed it");
        }

        [Test]
        public void NotifyChunkLanded_KillingBoomer_DoesNotFireBoomerDetonated()
        {
            // Only Burst and Bomb kills amplify (§6.5) — a Boomer crushed by a landing chunk just dies.
            var enemies = new EnemySystem(new GridModel(10, 10));
            var pos = new GridPos(0, 0);
            enemies.SpawnEnemy(EnemyType.Boomer, pos);

            bool fired = false;
            enemies.BoomerDetonated += (_, __, ___) => fired = true;

            enemies.NotifyChunkLanded(new List<GridPos> { pos });

            Assert.IsFalse(fired);
        }

        // ── Boomer death blast (§6.5, R5.8) ─────────────────────────────────
        // "Boomer killed → BoomerDetonated fires" is already pinned by
        // NotifyBurst_KillingBoomer_FiresBoomerDetonated_WithFallBonus and
        // NotifyBombBlast_KillingBoomer_FiresBoomerDetonated_WithChainMult above — not duplicated here.
        //
        // Every test below places the Boomer at (0,0) with the thing it should affect at (1,0), its
        // one cardinal neighbor that exists in a minimal 1-row grid. Above/Below/Left all fall
        // outside the grid and are silently skipped by the bounds check — only Right is exercised,
        // which is enough: the blast treats all four cardinal directions identically.

        [Test]
        public void Boomer_Explosion_DestroysAdjacentColorBlock()
        {
            var grid = GridModel.FromStringMap(new[] { ".A" }); // Boomer at (0,0), ColorA at (1,0)
            var enemies = new EnemySystem(grid);
            var boomerPos = new GridPos(0, 0);
            enemies.SpawnEnemy(EnemyType.Boomer, boomerPos);

            enemies.NotifyBurst(new List<GridPos> { boomerPos }, new List<GridPos>(), fallDist: 2);

            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 0)),
                "the adjacent color block must be destroyed by the Boomer's blast");
        }

        [Test]
        public void Boomer_Explosion_SoftensSteelToHard()
        {
            var grid = GridModel.FromStringMap(new[] { ".S" }); // Steel at (1,0)
            var enemies = new EnemySystem(grid);
            var boomerPos = new GridPos(0, 0);
            enemies.SpawnEnemy(EnemyType.Boomer, boomerPos);

            enemies.NotifyBurst(new List<GridPos> { boomerPos }, new List<GridPos>(), fallDist: 2);

            Assert.AreEqual(CellType.Hard, grid.Get(new GridPos(1, 0)),
                "Steel must soften to Hard, same as a real bomb blast — not be destroyed outright");
        }

        [Test]
        public void Boomer_Explosion_LiberatesAirCapsule()
        {
            var grid = GridModel.FromStringMap(new[] { ".P" }); // AirCapsule at (1,0)
            var enemies = new EnemySystem(grid);
            var boomerPos = new GridPos(0, 0);
            enemies.SpawnEnemy(EnemyType.Boomer, boomerPos);

            GridPos? liberated = null;
            enemies.AirCapsuleLiberated += p => liberated = p;

            enemies.NotifyBurst(new List<GridPos> { boomerPos }, new List<GridPos>(), fallDist: 2);

            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 0)));
            Assert.AreEqual(new GridPos(1, 0), liberated, "the capsule must be freed, not silently destroyed");
        }

        [Test]
        public void Boomer_Explosion_LiberatesDiamond()
        {
            var grid = GridModel.FromStringMap(new[] { ".D" }); // Diamond at (1,0)
            var enemies = new EnemySystem(grid);
            var boomerPos = new GridPos(0, 0);
            enemies.SpawnEnemy(EnemyType.Boomer, boomerPos);

            GridPos? liberated = null;
            enemies.DiamondLiberated += p => liberated = p;

            enemies.NotifyBurst(new List<GridPos> { boomerPos }, new List<GridPos>(), fallDist: 2);

            Assert.AreEqual(CellType.Empty, grid.Get(new GridPos(1, 0)));
            Assert.AreEqual(new GridPos(1, 0), liberated, "the diamond must be freed, not silently destroyed");
        }

        [Test]
        public void Boomer_Explosion_DoesNotArmAdjacentBomb()
        {
            var grid = GridModel.FromStringMap(new[] { ".X" }); // buried Bomb at (1,0)
            var enemies = new EnemySystem(grid);
            var boomerPos = new GridPos(0, 0);
            enemies.SpawnEnemy(EnemyType.Boomer, boomerPos);

            enemies.NotifyBurst(new List<GridPos> { boomerPos }, new List<GridPos>(), fallDist: 2);

            Assert.AreEqual(CellType.Bomb, grid.Get(new GridPos(1, 0)),
                "a Boomer blast must leave a buried bomb completely untouched — not arm it, not destroy it");
        }

        [Test]
        public void Boomer_Explosion_KillsAdjacentCrawler()
        {
            var grid = GridModel.FromStringMap(new[] { ".." });
            var enemies = new EnemySystem(grid);
            var boomerPos = new GridPos(0, 0);
            var crawlerPos = new GridPos(1, 0);
            enemies.SpawnEnemy(EnemyType.Boomer, boomerPos);
            int crawlerId = enemies.SpawnEnemy(EnemyType.Crawler, crawlerPos);

            var killed = new List<int>();
            enemies.EnemyKilled += (id, _, __, ___) => killed.Add(id);

            enemies.NotifyBurst(new List<GridPos> { boomerPos }, new List<GridPos>(), fallDist: 2);

            Assert.IsFalse(enemies.GetEnemyAt(crawlerPos).HasValue, "the adjacent Crawler must die in the blast");
            CollectionAssert.Contains(killed, crawlerId);
        }

        [Test]
        public void Boomer_Chain_KillingAnotherBoomer_AlsoDetonatesIt()
        {
            var grid = GridModel.FromStringMap(new[] { ".." });
            var enemies = new EnemySystem(grid);
            var posA = new GridPos(0, 0);
            var posB = new GridPos(1, 0);
            enemies.SpawnEnemy(EnemyType.Boomer, posA);
            enemies.SpawnEnemy(EnemyType.Boomer, posB);

            var detonated = new List<GridPos>();
            enemies.BoomerDetonated += (pos, bonus, _) => detonated.Add(pos);

            enemies.NotifyBurst(new List<GridPos> { posA }, new List<GridPos>(), fallDist: 4); // bonus = 2

            Assert.AreEqual(2, detonated.Count, "both A and the chain-killed B must detonate");
            CollectionAssert.Contains(detonated, posA);
            CollectionAssert.Contains(detonated, posB);
        }

        [Test]
        public void Boomer_ChainCap_FourDeep_FourthDoesNotDetonate()
        {
            // A(0,0) → B(1,0) → C(2,0) → D(3,0), each the next one's only cardinal neighbor.
            // Killing A cascades through B (depth 2) and C (depth 3, at the cap); D would be
            // depth 4 — it dies, but the cap stops it from detonating or cascading further.
            var grid = GridModel.FromStringMap(new[] { "...." });
            var enemies = new EnemySystem(grid);
            var posA = new GridPos(0, 0);
            var posB = new GridPos(1, 0);
            var posC = new GridPos(2, 0);
            var posD = new GridPos(3, 0);
            enemies.SpawnEnemy(EnemyType.Boomer, posA);
            enemies.SpawnEnemy(EnemyType.Boomer, posB);
            enemies.SpawnEnemy(EnemyType.Boomer, posC);
            enemies.SpawnEnemy(EnemyType.Boomer, posD);

            var detonated = new List<GridPos>();
            enemies.BoomerDetonated += (pos, bonus, _) => detonated.Add(pos);

            enemies.NotifyBurst(new List<GridPos> { posA }, new List<GridPos>(), fallDist: 4);

            Assert.AreEqual(3, detonated.Count, "the chain must cap at exactly 3 detonations (A, B, C)");
            CollectionAssert.Contains(detonated, posA);
            CollectionAssert.Contains(detonated, posB);
            CollectionAssert.Contains(detonated, posC);
            CollectionAssert.DoesNotContain(detonated, posD, "the 4th Boomer in the chain must not detonate — it's past the cap");

            Assert.IsFalse(enemies.GetEnemyAt(posD).HasValue, "D must still die, just without detonating");
        }

        [Test]
        public void Boomer_Explosion_NeverHitsTheAvatar()
        {
            var grid = GridModel.FromStringMap(new[] { ".." });
            var enemies = new EnemySystem(grid);
            var boomerPos = new GridPos(0, 0);
            enemies.SpawnEnemy(EnemyType.Boomer, boomerPos);

            bool avatarHit = false;
            enemies.AvatarHitByEnemy += _ => avatarHit = true;

            enemies.NotifyBurst(new List<GridPos> { boomerPos }, new List<GridPos>(), fallDist: 2);

            Assert.IsFalse(avatarHit, "a Boomer's blast is a bonus for the player, never a hazard — it must not fire avatar-harm events");
        }

        [Test]
        public void KillZone_MissingTheEnemy_DoesNotKillIt_NoEvent()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var pos = new GridPos(1, 1);
            enemies.SpawnEnemy(EnemyType.Crawler, pos);

            bool fired = false;
            enemies.EnemyKilled += (_, __, ___, ____) => fired = true;

            enemies.NotifyChunkLanded(new List<GridPos> { new GridPos(9, 9) });

            Assert.IsFalse(fired);
            Assert.IsTrue(enemies.GetEnemyAt(pos).HasValue);
        }

        [Test]
        public void AlreadyDeadEnemy_IsNotKilledAgain_NoDuplicateEvent()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            var pos = new GridPos(1, 1);
            enemies.SpawnEnemy(EnemyType.Crawler, pos);

            int killCount = 0;
            enemies.EnemyKilled += (_, __, ___, ____) => killCount++;

            enemies.NotifyChunkLanded(new List<GridPos> { pos });
            enemies.NotifyBombBlast(new List<GridPos> { pos }, chainMult: 1); // already dead, must be a no-op

            Assert.AreEqual(1, killCount);
        }

        // ── Reset ────────────────────────────────────────────────────────────

        [Test]
        public void Reset_ClearsAllEnemies()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            enemies.SpawnEnemy(EnemyType.Crawler, new GridPos(0, 0));
            enemies.SpawnEnemy(EnemyType.Boomer, new GridPos(1, 1));

            enemies.Reset();

            CollectionAssert.IsEmpty(enemies.GetAllAlive());
            Assert.IsNull(enemies.GetEnemyAt(new GridPos(0, 0)));
            Assert.IsNull(enemies.GetEnemyAt(new GridPos(1, 1)));
        }

        [Test]
        public void Reset_ThenSpawn_IdsRestartCleanly()
        {
            var enemies = new EnemySystem(new GridModel(10, 10));
            enemies.SpawnEnemy(EnemyType.Crawler, new GridPos(0, 0));
            enemies.Reset();

            int id = enemies.SpawnEnemy(EnemyType.Crawler, new GridPos(2, 2));

            Assert.AreEqual(1, enemies.GetAllAlive().Count);
            Assert.AreEqual(EnemyType.Crawler, enemies.GetEnemyAt(new GridPos(2, 2)).Value.Type);
            Assert.AreEqual(0, id, "the id sequence restarts after Reset");
        }
    }
}
