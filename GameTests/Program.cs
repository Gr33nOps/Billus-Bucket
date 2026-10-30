using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using BillusBucket;

namespace GameTests
{
    public static class Program
    {
        public static void Main()
        {
            TestNoteCatalog();
            TestRunScoring();
            TestCashFormatting();
            TestDropDirector();
            TestRoundFlow();
            TestRoundFlowLeaderboardRules();
            TestBilluAnimator();
            TestDropMotion();
            TestBucketControl();
            TestLeaderboardStore();
            TestLocalLeaderboardAndRestrictedStorage();
            TestBombHitSubmissionAndDuplicateGuard();
            TestCrazyGamesAdapterPolicies();
            TestPokiAdapterZeroNetworkByDefault();
            TestSharedOnlineAdapterAndOfflineFallback();
            TestPlatformLifecycleGuard();
            TestEdgeCaseSafety();

            Console.WriteLine("Billu's Bucket gameplay, platform SDK, and leaderboard checks passed.");
            DifficultyChecks.Run();
        }

        private static void Equal<T>(T expected, T actual, string behavior)
        {
            if (!Equals(expected, actual))
            {
                throw new Exception($"{behavior}: expected {expected}, got {actual}");
            }
        }

        private static void TestNoteCatalog()
        {
            int[] denominationCounts = new int[NoteCatalog.Count];
            for (int roll = 0; roll < NoteCatalog.RollCount; roll++)
            {
                denominationCounts[NoteCatalog.IndexForRoll(roll)]++;
            }

            for (int i = 1; i < denominationCounts.Length; i++)
            {
                bool isAvailableAndRarer = denominationCounts[i] > 0 && denominationCounts[i] < denominationCounts[i - 1];
                Equal(true, isAvailableAndRarer, "every higher note is available but strictly rarer");
                Equal(true, NoteCatalog.ValueAt(i) > NoteCatalog.ValueAt(i - 1), "sprite rows increase in value");
            }

            Equal(true, denominationCounts[0] + denominationCounts[1] <= 500, "small notes occupy at most half the reward pool");
            Equal(true, denominationCounts[4] + denominationCounts[5] + denominationCounts[6] >= 140, "valuable notes appear regularly");
            Equal(5000, NoteCatalog.ValueAt(NoteCatalog.IndexForRoll(999)), "rarest roll awards the Rs5000 note");
        }

        private static void TestRunScoring()
        {
            BillusBucketRun rupees = new BillusBucketRun();
            foreach (int denomination in new[] { 10, 20, 50, 100, 500, 1000, 5000 })
            {
                rupees.CatchCash(denomination);
            }

            Equal("6680", rupees.Cash.ToString(), "mixed rupee denominations award their own value");
            Equal("6680", rupees.HitBomb().ToString(), "rupee run total survives in game-over summary");

            BillusBucketRun run = new BillusBucketRun();
            Equal("10", run.CatchCash().ToString(), "one cash catch earns ten rupees");
            Equal("20", run.CatchCash().ToString(), "two catches total twenty rupees");
            Equal("30", run.CatchCash().ToString(), "third note keeps its current value");
            Equal("40", run.CatchCash().ToString(), "fourth note keeps its current value");
            Equal("50", run.CatchCash().ToString(), "fifth note still pays ten rupees");
            Equal("60", run.CatchCash().ToString(), "sixth note still pays ten rupees");

            for (int i = 0; i < 5; i++)
            {
                run.CatchCash();
            }

            Equal("110", run.Cash.ToString(), "eleven notes total 110");
            Equal("120", run.CatchCash().ToString(), "twelfth note still pays ten rupees");
            Equal("120", run.HitBomb().ToString(), "bomb records cash earned in the run");
            Equal("0", run.Cash.ToString(), "bomb wipes current cash");
            Equal(true, run.IsOver, "bomb ends the run");
            Equal("120", run.BestCash.ToString(), "bomb preserves best score");
            Equal("0", run.CatchCash().ToString(), "cash cannot be caught after explosion");

            run.Restart();
            Equal("0", run.Cash.ToString(), "retry starts with no cash");
            Equal(false, run.IsOver, "retry immediately resumes play");
            Equal("120", run.BestCash.ToString(), "retry keeps best score");
            Equal("10", run.CatchCash().ToString(), "retry resets note value to ten rupees");

            BillusBucketRun endless = new BillusBucketRun();
            for (int i = 0; i < 2840; i++)
            {
                endless.CatchCash();
            }

            Equal("28400", endless.Cash.ToString(), "long runs keep every note at ten rupees");
        }

        private static void TestCashFormatting()
        {
            Equal("999", CashDisplay.Compact(999), "small values remain exact");
            Equal("1K", CashDisplay.Compact(1000), "thousand-rupee notes remain readable exactly");
            Equal("1.25K", CashDisplay.Compact(1250), "thousands use short notation");
            Equal("2K", CashDisplay.Compact(2000), "two thousand remains readable");
            Equal("99.9K", CashDisplay.Compact(99999), "below lakh does not overstate the score");
            Equal("1L", CashDisplay.Compact(100000), "one lakh boundary");
            Equal("12.3L", CashDisplay.Compact(1230000), "large totals use familiar lakh notation");
            Equal("1Cr", CashDisplay.Compact(10000000), "one crore boundary");
            Equal("117Cr", CashDisplay.Compact(1170000000), "historical large scores remain compact");
            Equal("2.36E21", CashDisplay.Compact(BigInteger.Parse("2360000000000000000000")),
                "unbounded totals stay readable in narrow panels");
        }

        private static void TestDropDirector()
        {
            DropDirector director = new DropDirector(42);
            float time = 0f;
            float previousBomb = -100f;
            int bombs = 0;

            for (int i = 0; i < 2000; i++)
            {
                DropPlan next = director.Next(time, 66f, 252f);
                Equal(true, next.X >= 66f && next.X <= 252f, "all drops stay within spawn bounds");
                Equal(true, next.Delay >= 0.24f && next.Delay <= 1.1f, "spawn tempo stays bounded");

                if (next.Kind == DropKind.Bomb)
                {
                    Equal(true, time >= 4.5f && time - previousBomb >= 0.619f, "opening grace and bomb spacing");
                    previousBomb = time;
                    bombs++;
                }

                float maxSpeed = next.Kind == DropKind.Bomb ? 120f : 54f;
                Equal(true, next.Speed <= maxSpeed, "speed has a fair ceiling");
                time += next.Delay;
            }

            Equal(true, bombs > 100, "long runs still contain hazards");
        }

        private static void TestRoundFlow()
        {
            RoundFlow flow = new RoundFlow();
            Equal(RoundPhase.Ready, flow.Phase, "game opens ready for the play button");
            Equal(RoundAction.StartRun, flow.TryMainActionAt(160, 94),
                "centered play button starts a run on the 320 x 180 canvas");

            flow.HitBomb();
            flow.ShowReady();
            Equal(RoundAction.None, flow.TryMainActionAt(20, 200),
                "clicking the playfield outside the button does not start a run");
            Equal(RoundPhase.Ready, flow.Phase, "outside click leaves the start screen visible");

            ButtonBounds play = flow.MainButton;
            Equal(RoundAction.StartRun, flow.TryMainActionAt(play.X + 8, play.Y + 8), "play button begins a run");
            Equal(RoundPhase.Playing, flow.Phase, "play button enters gameplay");
            Equal(RoundAction.None, flow.TryMainActionAt(play.X + 8, play.Y + 8),
                "gameplay clicks cannot start another run");

            flow.TogglePause();
            Equal(RoundPhase.Paused, flow.Phase, "pause key stops gameplay");
            Equal(RoundAction.None, flow.TryMainActionAt(20, 200),
                "clicking outside resume keeps the game paused");

            ButtonBounds resume = flow.MainButton;
            Equal(RoundAction.Resume, flow.TryMainActionAt(resume.X + 8, resume.Y + 8),
                "resume button continues the same run");
            Equal(RoundPhase.Playing, flow.Phase, "resume returns to gameplay");

            flow.HitBomb();
            Equal(RoundPhase.GameOver, flow.Phase, "bomb ends gameplay");
            flow.TogglePause();
            Equal(RoundPhase.GameOver, flow.Phase, "pause cannot revive an ended run");
            Equal(RoundAction.None, flow.TryMainActionAt(20, 200),
                "clicking outside retry leaves game over visible");

            ButtonBounds retry = flow.MainButton;
            Equal(RoundAction.StartRun, flow.TryMainActionAt(retry.X + 8, retry.Y + 8),
                "play again button starts a fresh run immediately");
            Equal(RoundPhase.Playing, flow.Phase, "play again returns directly to gameplay");

            flow.HitBomb();
            flow.ShowReady();
            Equal(RoundPhase.Ready, flow.Phase, "ShowReady returns from GameOver to Ready");

            flow.ActivateMainAction();
            flow.OnFocusLost();
            Equal(RoundPhase.Paused, flow.Phase, "switching away pauses a live run");
            flow.OnFocusLost();
            Equal(RoundPhase.Paused, flow.Phase, "repeated focus loss never resumes gameplay");

            flow.Reset();
            Equal(RoundPhase.Ready, flow.Phase, "Reset returns directly to Ready even from Paused");

            flow.ActivateMainAction();
            flow.HitBomb();
            flow.OnFocusLost();
            Equal(RoundPhase.GameOver, flow.Phase, "focus loss cannot change an ended run");
        }

        private static void TestRoundFlowLeaderboardRules()
        {
            RoundFlow flow = new RoundFlow();
            Equal(false, flow.IsLeaderboardOpen, "leaderboard starts closed");

            // Accessible from Ready screen via the top-right trophy icon button (below speaker button)
            ButtonBounds readyLb = flow.LeaderboardButton;
            Equal(292, readyLb.X, "leaderboard icon aligns with top-right audio buttons");
            Equal(50, readyLb.Y, "leaderboard icon sits directly below the speaker button");
            Equal(true, readyLb.Width > 0, "Ready screen exposes top-right leaderboard icon button");
            Equal(true, flow.TryOpenLeaderboardAt(readyLb.X + 6, readyLb.Y + 6), "leaderboard opens from Ready");
            Equal(true, flow.IsLeaderboardOpen, "leaderboard state is open");
            Equal(RoundAction.None, flow.ActivateMainAction(), "main action will not start run behind open leaderboard");
            Equal(true, flow.TryCloseLeaderboardAt(readyLb.X + 6, readyLb.Y + 6), "clicking top-right leaderboard icon again toggles it closed");
            Equal(false, flow.IsLeaderboardOpen, "leaderboard toggled closed");

            Equal(true, flow.TryOpenLeaderboardAt(readyLb.X + 6, readyLb.Y + 6), "leaderboard re-opens from Ready");
            ButtonBounds back = flow.BackButton;
            Equal(true, flow.TryCloseLeaderboardAt(back.X + 6, back.Y + 6), "Back button closes leaderboard");
            Equal(false, flow.IsLeaderboardOpen, "leaderboard closes back to Ready");
            Equal(RoundPhase.Ready, flow.Phase, "closing leaderboard preserves Ready phase");

            // Never overlays active gameplay or paused gameplay
            flow.ActivateMainAction();
            Equal(RoundPhase.Playing, flow.Phase, "entered Playing");
            Equal(false, flow.OpenLeaderboard(), "leaderboard refuses to open over active gameplay");
            Equal(0, flow.LeaderboardButton.Width, "no leaderboard button during active gameplay");

            flow.TogglePause();
            Equal(RoundPhase.Paused, flow.Phase, "entered Paused");
            Equal(false, flow.OpenLeaderboard(), "leaderboard refuses to open over paused gameplay");

            // Accessible from GameOver screen, and Play Again restarts immediately after closing
            flow.TogglePause();
            flow.HitBomb();
            Equal(RoundPhase.GameOver, flow.Phase, "entered GameOver");
            ButtonBounds gameOverLb = flow.LeaderboardButton;
            Equal(true, flow.TryOpenLeaderboardAt(gameOverLb.X + 6, gameOverLb.Y + 6), "leaderboard opens from GameOver");
            Equal(true, flow.CloseLeaderboard(), "closing leaderboard returns to GameOver");
            Equal(RoundPhase.GameOver, flow.Phase, "phase remains GameOver");
            Equal(RoundAction.StartRun, flow.ActivateMainAction(), "Play Again immediately restarts from GameOver");
        }

        private static void TestBilluAnimator()
        {
            BilluAnimator billu = new BilluAnimator();
            Equal(BilluAction.Straight, billu.Action, "Billu faces forward while still");
            Equal(0, billu.Frame, "straight pose stays on one frame");

            billu.Update(0.15f, 160f);
            Equal(BilluAction.WalkRight, billu.Action, "Billu walks right with the bucket");
            Equal(true, billu.Frame > 0, "walking cycles animation frames");

            billu.Update(0.15f, -160f);
            Equal(BilluAction.WalkLeft, billu.Action, "Billu walks left with the bucket");

            billu.Update(0.15f, 0f);
            Equal(BilluAction.Straight, billu.Action, "Billu faces forward when he stops");
            Equal(0, billu.Frame, "stopping returns to the front sprite");

            billu.Burn();
            Equal(BilluAction.Burned, billu.Action, "bomb leaves Billu burned");
            billu.Update(1f, 160f);
            Equal(BilluAction.Burned, billu.Action, "burned pose remains through game over");
            Equal(0, billu.Frame, "burned face is one combined sprite");

            billu.Reset();
            Equal(BilluAction.Straight, billu.Action, "retry restores Billu");
        }

        private static void TestDropMotion()
        {
            DropMotion breezeA = new DropMotion(DropKind.Cash, 100f, 0f, 35f, seed: 11);
            DropMotion breezeB = new DropMotion(DropKind.Cash, 100f, 0f, 35f, seed: 22);
            DropMotion stepped = new DropMotion(DropKind.Cash, 100f, 0f, 35f, seed: 11);

            breezeA.Update(2f);
            breezeB.Update(2f);
            for (int i = 0; i < 120; i++)
            {
                stepped.Update(1f / 60f);
            }

            Equal(true, Math.Abs(breezeA.X - breezeB.X) > 0.1f, "notes have distinct wind paths");
            Equal(true, Math.Abs(breezeA.X - stepped.X) < 0.01f && Math.Abs(breezeA.Y - stepped.Y) < 0.01f,
                "flutter is independent of frame rate");

            for (int seed = 0; seed < 60; seed++)
            {
                DropMotion note = new DropMotion(DropKind.Cash, 66f, -12f, 35f, seed);
                for (int frame = 0; frame < 600; frame++)
                {
                    float previousY = note.Y;
                    note.Update(1f / 60f);
                    Equal(true, Math.Abs(note.X - 66f) <= 92f, "wind displacement remains bounded");
                    Equal(true, note.Y > previousY, "notes continue falling during flutter");
                }
            }

            DropMotion windy = new DropMotion(DropKind.Cash, 160f, -12f, 35f, seed: 11);
            float minWindX = 160f;
            float maxWindX = 160f;
            for (int i = 0; i < 240; i++)
            {
                windy.Update(1f / 60f);
                minWindX = Math.Min(minWindX, windy.X);
                maxWindX = Math.Max(maxWindX, windy.X);
            }
            Equal(true, maxWindX - minWindX > 25f, "wind creates visible broad sweeps during a fall");

            DropMotion edgeNote = new DropMotion(DropKind.Cash, 66f, -12f, 35f, seed: 5, left: 59f, right: 259f);
            for (int i = 0; i < 600; i++)
            {
                edgeNote.Update(1f / 60f);
                Equal(true, edgeNote.X >= 59f && edgeNote.X <= 259f, "gusts keep notes inside the play area");
            }

            DropMotion cash = new DropMotion(DropKind.Cash, 100f, 0f, 72f);
            DropMotion bomb = new DropMotion(DropKind.Bomb, 100f, 0f, 118f);
            cash.Update(1f);
            bomb.Update(1f);
            Equal(true, cash.Y > 63f && cash.Y < 81f, "cash falls gently with a small flutter variation");
            Equal(118f, bomb.Y, "bomb falls faster");
            Equal(true, cash.X != 100f, "cash sways sideways");
            Equal(100f, bomb.X, "bomb falls straight down");
        }

        private static void TestBucketControl()
        {
            BucketControl sliding = new BucketControl(160f, 1000f, 24f);
            sliding.Update(0.5f, 1, null);
            float releaseX = sliding.X;
            sliding.Update(0.15f, 0, null);
            Equal(true, sliding.VelocityX > 25f, "release retains a short readable slide");
            sliding.Update(0.15f, 0, null);
            Equal(true, sliding.VelocityX == 0f && sliding.X - releaseX > 13f && sliding.X - releaseX < 18f,
                "slide stops within a controlled distance");
            sliding.Update(0.5f, 1, null);
            sliding.Update(0.15f, -1, null);
            Equal(true, sliding.VelocityX > 0f, "reversing needs a brief braking commitment");
            sliding.Update(0.1f, -1, null);
            Equal(true, sliding.VelocityX < 0f, "reversal remains responsive within a quarter second");
            BucketControl bucket = new BucketControl(180f, 360f, 44f);
            bucket.Update(1f / 60f, 1, null);
            Equal(true, bucket.X > 180f && bucket.X < 184f,
                "bucket starts moving without instantly reaching running speed");

            float firstStep = bucket.X - 180f;
            float beforeSecondStep = bucket.X;
            bucket.Update(1f / 60f, 1, null);
            Equal(true, bucket.X - beforeSecondStep > firstStep,
                "bucket accelerates while the direction key is held");

            for (int i = 0; i < 12; i++)
            {
                bucket.Update(1f / 60f, 1, null);
            }

            float beforeRelease = bucket.X;
            bucket.Update(1f / 60f, 0, null);
            Equal(true, bucket.X > beforeRelease, "bucket keeps a little momentum after release");

            for (int i = 0; i < 25; i++)
            {
                bucket.Update(1f / 60f, 0, null);
            }

            float settled = bucket.X;
            bucket.Update(1f / 60f, 0, null);
            Equal(true, Math.Abs(bucket.X - settled) < 0.01f,
                "bucket comes to rest instead of sliding forever");

            for (int i = 0; i < 20; i++)
            {
                bucket.Update(1f / 60f, -1, null);
            }
            Equal(true, bucket.X < settled, "bucket can reverse direction after settling");

            BucketControl turning = new BucketControl(120f, 360f, 44f);
            for (int i = 0; i < 20; i++)
            {
                turning.Update(1f / 60f, 1, null);
            }
            float beforeTurn = turning.X;
            turning.Update(1f / 60f, -1, null);
            Equal(true, turning.X > beforeTurn,
                "changing direction slows a moving bucket before it runs the other way");

            for (int i = 0; i < 16; i++)
            {
                turning.Update(1f / 60f, -1, null);
            }
            Equal(true, turning.VelocityX < 0f && turning.Facing == -1,
                "running direction is available for character animation");
            Equal(true, turning.RunAmount > 0f,
                "running speed is available for character animation");

            BucketControl rightEdge = new BucketControl(180f, 360f, 44f);
            for (int i = 0; i < 120; i++)
            {
                rightEdge.Update(1f / 60f, 1, null);
            }
            Equal(338f, rightEdge.X, "keyboard movement stays inside the playfield");

            BucketControl dragged = new BucketControl(180f, 360f, 44f);
            dragged.Update(1f / 60f, 0, 0f);
            Equal(true, dragged.X < 180f && dragged.X > 170f,
                "touch or mouse target moves the bucket without teleporting");

            for (int i = 0; i < 120; i++)
            {
                dragged.Update(1f / 60f, 0, 0f);
            }
            Equal(true, Math.Abs(dragged.X - 22f) < 0.5f,
                "touch or mouse target settles at the clamped left edge");

            for (int i = 0; i < 180; i++)
            {
                dragged.Update(1f / 60f, 0, 500f);
            }
            Equal(true, Math.Abs(dragged.X - 338f) < 0.5f,
                "touch or mouse target settles at the clamped right edge");

            BucketControl rooftopBucket = new BucketControl(160f, 218f, 24f, 50f);
            rooftopBucket.Update(5f, -1, null);
            Equal(62f, rooftopBucket.X, "left edge keeps the body beyond the plant");
            rooftopBucket.Update(5f, 1, null);
            Equal(256f, rooftopBucket.X, "right edge keeps the body before the stand");
            rooftopBucket.Update(5f, 0, -500f);
            Equal(62f, rooftopBucket.X, "pointer cannot cross the plant");
            rooftopBucket.Update(5f, 0, 500f);
            Equal(256f, rooftopBucket.X, "pointer cannot cross the stand");
        }

        private static void TestLeaderboardStore()
        {
            string folder = Path.Combine(Path.GetTempPath(), "billus-bucket-tests-" + Guid.NewGuid());
            try
            {
                string file = Path.Combine(folder, "scores.json");
                LeaderboardStore scores = new LeaderboardStore(file);
                scores.Record("Deferred", 30, saveImmediately: false);
                Equal(false, File.Exists(file), "catch updates best without synchronous disk writes");
                Equal("30", scores.BestFor("Deferred").ToString(), "deferred best appears immediately");

                scores.Flush();
                Equal("30", new LeaderboardStore(file).BestFor("Deferred").ToString(), "flush persists deferred scores");
                File.Delete(file);

                scores = new LeaderboardStore(file);
                string blockedPath = Path.Combine(folder, "blocked");
                Directory.CreateDirectory(blockedPath);
                LeaderboardStore blockedScores = new LeaderboardStore(blockedPath);
                blockedScores.Record("Player", 100);
                Equal("100", blockedScores.BestFor("Player").ToString(), "save failure preserves best in memory");
                Equal(true, blockedScores.LastSaveError != null, "save failure is available for diagnostics");

                Equal(true, scores.Record("CashKing", 120), "first score is saved");
                Equal(false, scores.Record("CashKing", 90), "lower score cannot replace best");
                Equal(true, scores.Record("PixelPro", 180), "second player score is saved");
                Equal(false, scores.Record("Nobody", 0), "zero score is not ranked");
                Equal(2, scores.TopScores(10).Count, "leaderboard has two real players");
                Equal("PIXELPRO", scores.TopScores(10)[0].Name, "leaderboard sorts highest first");
                Equal("120", scores.BestFor("cashking").ToString(), "names are matched consistently");

                LeaderboardStore reloaded = new LeaderboardStore(file);
                Equal("180", reloaded.BestFor("PixelPro").ToString(), "scores survive reopening the game");
                Equal(true, reloaded.Record("CashKing", 250), "higher personal score replaces old best");
                Equal("250", new LeaderboardStore(file).TopScores(1)[0].BestCash.ToString(),
                    "updated best is persisted and ranked first");

                File.WriteAllText(file, "[{\"Name\":\"SAVED\",\"BestCash\":120}]");
                Equal("120", new LeaderboardStore(file).BestFor("Saved").ToString(),
                    "existing numeric leaderboard saves remain readable");

                File.WriteAllText(file, "[null,42,{\"Name\":\"OK\",\"BestCash\":50}]");
                Equal("50", new LeaderboardStore(file).BestFor("OK").ToString(),
                    "invalid individual save entries do not crash loading or discard valid scores");

                File.WriteAllText(file, "[{\"Name\":\"player\",\"BestCash\":50},{\"Name\":\"PLAYER\",\"BestCash\":200}]");
                LeaderboardStore deduplicated = new LeaderboardStore(file);
                Equal(1, deduplicated.TopScores(10).Count,
                    "duplicate normalized player names are merged on load");
                Equal("200", deduplicated.BestFor("PLAYER").ToString(),
                    "merged duplicate entries keep the highest score");

                File.WriteAllText(file, "[{\"Name\":\"MEGA\",\"BestCash\":\"11805916207174113034240\"}]");
                LeaderboardStore hugeScores = new LeaderboardStore(file);
                Equal("11805916207174113034240", hugeScores.BestFor("Mega").ToString(),
                    "leaderboard loads scores beyond fixed-size integer limits");
                Equal(true, hugeScores.Record("Other", 10), "new score saves alongside huge score");
                Equal("11805916207174113034240", new LeaderboardStore(file).BestFor("Mega").ToString(),
                    "huge score survives a subsequent save");
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
        }

        private static void TestLocalLeaderboardAndRestrictedStorage()
        {
            InMemoryScoreStorage storage = new InMemoryScoreStorage(initialBest: 500);
            LocalLeaderboardService local = new LocalLeaderboardService(storage);
            local.InitializeAsync().GetAwaiter().GetResult();

            Equal("500", local.GetPersonalBest().ToString(), "local adapter loads initial personal best");
            Equal(false, local.Capabilities.SupportsWeeklyTop10, "local adapter does not claim global rankings");
            Equal(0, local.CurrentSnapshot.WeeklyTop10.Count, "local adapter never fabricates fake global entries");
            Equal(null, local.CurrentSnapshot.CurrentPlayerWeeklyRank, "local adapter never fabricates player rank");

            // Simulate restricted browser storage throwing SecurityError / QuotaExceededError on every call
            ThrowingScoreStorage restricted = new ThrowingScoreStorage();
            LocalLeaderboardService restrictedService = new LocalLeaderboardService(restricted);
            ScoreSubmissionResult res = restrictedService.SubmitRunScoreAsync("run-restricted-1", 1250, 18f)
                .GetAwaiter().GetResult();
            Equal(true, res.Accepted, "restricted storage still records completed run in session memory");
            Equal("1250", restrictedService.GetPersonalBest().ToString(), "personal best survives restricted storage");
            Equal("SESSION ONLY - STORAGE RESTRICTED", restrictedService.CurrentSnapshot.StatusMessage,
                "restricted storage reports honest session-only status");
        }

        private static void TestBombHitSubmissionAndDuplicateGuard()
        {
            CountingTransport transport = new CountingTransport();
            SharedOnlineLeaderboardService online = new SharedOnlineLeaderboardService(
                new InMemoryScoreStorage(),
                transport,
                sourcePlatform: "web");

            // Simulate catching rupees and then hitting a bomb (wiping run.Cash to 0 while returning pre-explosion cash)
            BillusBucketRun run = new BillusBucketRun();
            run.CatchCash(500);
            run.CatchCash(1000);
            BigInteger preExplosionCash = run.HitBomb();

            Equal("0", run.Cash.ToString(), "bomb wipes active bucket cash");
            Equal("1500", preExplosionCash.ToString(), "pre-explosion cash is captured before reset");

            ScoreSubmissionResult firstSubmit = online.SubmitRunScoreAsync("run-bomb-001", preExplosionCash, 22.5f)
                .GetAwaiter().GetResult();
            Equal(true, firstSubmit.Accepted, "first submission for completed run is accepted");
            Equal(false, firstSubmit.WasDuplicateRun, "first submission is not marked duplicate");
            Equal("1500", online.GetPersonalBest().ToString(), "pre-explosion score updates personal best");
            Equal(1, transport.SubmitCount, "exactly one network submission sent for completed run");

            // Attempt duplicate submission of the exact same completed run
            ScoreSubmissionResult duplicateSubmit = online.SubmitRunScoreAsync("run-bomb-001", preExplosionCash, 22.5f)
                .GetAwaiter().GetResult();
            Equal(false, duplicateSubmit.Accepted, "duplicate submission for same runId is rejected");
            Equal(true, duplicateSubmit.WasDuplicateRun, "duplicate submission is flagged");
            Equal(1, transport.SubmitCount, "duplicate runId never triggers a second network call");
        }

        private static void TestCrazyGamesAdapterPolicies()
        {
            // 1. Uninvited game: must NOT pretend leaderboard access is active or call SDK submitScore
            FakeCrazyGamesClient uninvitedSdk = new FakeCrazyGamesClient(ready: true);
            CrazyGamesLeaderboardService uninvited = new CrazyGamesLeaderboardService(
                new InMemoryScoreStorage(),
                uninvitedSdk,
                leaderboardAccessGranted: false);

            Equal(false, uninvited.Capabilities.SupportsScoreSubmission,
                "uninvited CrazyGames build does not claim native score submission");
            Equal(false, uninvited.Capabilities.UsesNativePortalOverlay,
                "uninvited CrazyGames build does not claim native portal leaderboard");

            ScoreSubmissionResult uninvitedRes = uninvited.SubmitRunScoreAsync("cg-run-1", 800, 15f)
                .GetAwaiter().GetResult();
            Equal(true, uninvitedRes.Accepted, "uninvited CrazyGames build still saves personal best locally");
            Equal("800", uninvited.GetPersonalBest().ToString(), "personal best tracked locally on CrazyGames");
            Equal(0, uninvitedSdk.SubmitCalls, "uninvited CrazyGames build makes zero SDK submitScore calls");

            // 2. Invited game with encryption key ready: submits encrypted score once per completed run
            FakeCrazyGamesClient invitedSdk = new FakeCrazyGamesClient(ready: true);
            CrazyGamesLeaderboardService invited = new CrazyGamesLeaderboardService(
                new InMemoryScoreStorage(),
                invitedSdk,
                leaderboardAccessGranted: true);

            Equal(true, invited.Capabilities.SupportsScoreSubmission,
                "invited CrazyGames build enables native score submission");
            Equal(true, invited.Capabilities.UsesNativePortalOverlay,
                "invited CrazyGames build marks native portal overlay active");

            ScoreSubmissionResult invitedRes = invited.SubmitRunScoreAsync("cg-run-2", 2400, 31f)
                .GetAwaiter().GetResult();
            Equal(true, invitedRes.SubmittedOnline, "invited CrazyGames build submits score through JS SDK");
            Equal(1, invitedSdk.SubmitCalls, "invited CrazyGames build calls SDK submitScore once");
            Equal(2400L, invitedSdk.LastSubmittedScore, "exact pre-explosion score is passed to CrazyGames SDK");

            // Duplicate runId must not re-submit to CrazyGames SDK
            invited.SubmitRunScoreAsync("cg-run-2", 2400, 31f).GetAwaiter().GetResult();
            Equal(1, invitedSdk.SubmitCalls, "CrazyGames adapter enforces once-per-run submission");
        }

        private static void TestPokiAdapterZeroNetworkByDefault()
        {
            CountingTransport spyTransport = new CountingTransport();

            // Even if a shared endpoint/transport object exists, Poki must NOT touch it when unapproved!
            PokiLeaderboardService defaultPoki = new PokiLeaderboardService(
                new InMemoryScoreStorage(),
                isApprovedByPokiCsp: false,
                approvedExternalTransport: spyTransport);

            defaultPoki.InitializeAsync().GetAwaiter().GetResult();
            defaultPoki.RefreshRankingsAsync().GetAwaiter().GetResult();
            ScoreSubmissionResult pokiLocalRes = defaultPoki.SubmitRunScoreAsync("poki-run-1", 950, 19f)
                .GetAwaiter().GetResult();

            Equal(false, defaultPoki.IsExternalBackendEnabled, "Poki external backend disabled by default");
            Equal(0, spyTransport.FetchCount, "unapproved Poki build makes zero external fetch calls");
            Equal(0, spyTransport.SubmitCount, "unapproved Poki build makes zero external submit calls");
            Equal(true, pokiLocalRes.Accepted, "Poki build saves personal best locally without network");
            Equal("950", defaultPoki.GetPersonalBest().ToString(), "Poki local personal best is preserved");

            // Factory verification with PlatformRuntimeConfig
            using HttpClient dummyHttp = new HttpClient();
            PlatformRuntimeConfig pokiUnapprovedConfig = new PlatformRuntimeConfig
            {
                ActivePlatform = "poki",
                PokiApprovedExternalLeaderboardEnabled = false,
                SharedLeaderboardEnabled = true,
                SharedLeaderboardEndpointUrl = "https://example.supabase.co/functions/v1/leaderboard"
            };
            ILeaderboardService fromFactory = LeaderboardServiceFactory.Create(
                pokiUnapprovedConfig,
                new InMemoryScoreStorage(),
                httpClient: dummyHttp);
            Equal(false, fromFactory.Capabilities.SupportsWeeklyTop10,
                "factory blocks external leaderboard on Poki until PokiApprovedExternalLeaderboardEnabled is true");

            // Approved Poki build delegates cleanly to approved external transport
            PokiLeaderboardService approvedPoki = new PokiLeaderboardService(
                new InMemoryScoreStorage(),
                isApprovedByPokiCsp: true,
                approvedExternalTransport: spyTransport);

            approvedPoki.InitializeAsync().GetAwaiter().GetResult();
            ScoreSubmissionResult approvedRes = approvedPoki.SubmitRunScoreAsync("poki-run-2", 3100, 40f)
                .GetAwaiter().GetResult();
            Equal(true, approvedPoki.IsExternalBackendEnabled, "CSP-approved Poki build enables external leaderboard");
            Equal(1, spyTransport.FetchCount, "approved Poki build fetches weekly Top 10");
            Equal(1, spyTransport.SubmitCount, "approved Poki build submits completed run score");
            Equal(true, approvedRes.SubmittedOnline, "approved Poki submission succeeds");
            Equal(4, approvedRes.WeeklyRank, "approved Poki receives player weekly rank");
        }

        private static void TestSharedOnlineAdapterAndOfflineFallback()
        {
            CountingTransport offlineTransport = new CountingTransport { SimulateOffline = true };
            SharedOnlineLeaderboardService service = new SharedOnlineLeaderboardService(
                new InMemoryScoreStorage(initialBest: 400),
                offlineTransport,
                sourcePlatform: "web");

            service.InitializeAsync().GetAwaiter().GetResult();
            Equal(false, service.CurrentSnapshot.IsOnlineAvailable, "offline transport marks online unavailable");
            Equal("400", service.GetPersonalBest().ToString(), "offline init preserves local personal best");

            ScoreSubmissionResult offlineSubmit = service.SubmitRunScoreAsync("offline-run-1", 1800, 25f)
                .GetAwaiter().GetResult();
            Equal(true, offlineSubmit.Accepted, "offline run submission still succeeds locally");
            Equal(false, offlineSubmit.SubmittedOnline, "offline run honestly reports not submitted online");
            Equal("1800", service.GetPersonalBest().ToString(), "offline run updates local personal best");
        }

        private static void TestPlatformLifecycleGuard()
        {
            RecordingPlatformBridge raw = new RecordingPlatformBridge();
            PlatformLifecycleGuard guard = new PlatformLifecycleGuard(raw);

            // Duplicate loading calls fire once; gameplayStop while inactive is ignored
            guard.NotifyGameplayStop();
            guard.NotifyLoadingStarted();
            guard.NotifyLoadingStarted();
            guard.NotifyLoadingFinished();
            guard.NotifyLoadingFinished();

            // Consecutive gameplayStart calls fire once; consecutive gameplayStop calls fire once
            guard.NotifyGameplayStart();
            guard.NotifyGameplayStart();
            guard.NotifyGameplayStop();
            guard.NotifyGameplayStop();

            string expectedOrder = "loadStart,loadFinish,playStart,playStop";
            Equal(expectedOrder, string.Join(",", raw.Events),
                "PlatformLifecycleGuard enforces strict portal SDK event ordering without duplicates");
        }

        private static void TestEdgeCaseSafety()
        {
            // Zero-width or inverted wind bounds must never produce NaN coordinates.
            DropMotion collapsedBounds = new DropMotion(DropKind.Cash, 100f, 0f, 35f, seed: 3, left: 120f, right: 120f);
            collapsedBounds.Update(0.5f);
            Equal(true, float.IsFinite(collapsedBounds.X) && collapsedBounds.X == 120f,
                "collapsed wind bounds stay finite without division by zero");

            // Bomb drops should report zero rupee value even if constructed directly.
            Drop bombDrop = new Drop(new DropMotion(DropKind.Bomb, 100f, 0f, 60f));
            Equal(0, bombDrop.Value, "bomb drop reports zero rupee value");

            Drop rs50Drop = new Drop(new DropMotion(DropKind.Cash, 100f, 0f, 40f), noteIndex: 2);
            Equal(50, rs50Drop.Value, "cash drop reports its catalog value");

            // Non-finite frame deltas and narrow playfields must not crash or hang BucketControl.
            BucketControl narrowBucket = new BucketControl(startX: 50f, playfieldWidth: 10f, bucketWidth: 24f, playfieldLeft: 40f);
            narrowBucket.Update(float.PositiveInfinity, 1, null);
            narrowBucket.Update(float.NaN, 1, float.NaN);
            Equal(true, float.IsFinite(narrowBucket.X), "BucketControl ignores non-finite dt and handles narrow bounds");
        }

        // =====================================================================
        // Test Doubles
        // =====================================================================

        private sealed class ThrowingScoreStorage : ILocalScoreStorage
        {
            public bool IsPersistentStorageAvailable => throw new UnauthorizedAccessException("Storage blocked");
            public BigInteger LoadPersonalBest() => throw new UnauthorizedAccessException("Storage blocked");
            public void SavePersonalBest(BigInteger bestScore, bool flushImmediately = true) =>
                throw new UnauthorizedAccessException("Storage blocked");
            public string GetOrCreateAnonymousPlayerId() => throw new UnauthorizedAccessException("Storage blocked");
            public string GetOrCreateSafeDisplayName() => throw new UnauthorizedAccessException("Storage blocked");
            public void Flush() => throw new UnauthorizedAccessException("Storage blocked");
        }

        private sealed class FakeCrazyGamesClient : ICrazyGamesSdkClient
        {
            public bool IsSdkInitialized { get; }
            public bool IsNativeLeaderboardReady { get; }
            public int SubmitCalls { get; private set; }
            public long LastSubmittedScore { get; private set; }

            public FakeCrazyGamesClient(bool ready)
            {
                IsSdkInitialized = ready;
                IsNativeLeaderboardReady = ready;
            }

            public Task<bool> SubmitEncryptedScoreAsync(long score)
            {
                SubmitCalls++;
                LastSubmittedScore = score;
                return Task.FromResult(true);
            }
        }

        private sealed class CountingTransport : ISharedLeaderboardTransport
        {
            public bool SimulateOffline { get; set; }
            public int FetchCount { get; private set; }
            public int SubmitCount { get; private set; }

            public Task<SharedLeaderboardRemoteResponse> FetchWeeklyTop10Async(
                string anonymousPlayerId,
                string sourcePlatform)
            {
                FetchCount++;
                if (SimulateOffline)
                {
                    return Task.FromResult(new SharedLeaderboardRemoteResponse(
                        false,
                        errorMessage: "OFFLINE - LOCAL BEST ACTIVE"));
                }

                IReadOnlyList<LeaderboardRankEntry> sample = new[]
                {
                    new LeaderboardRankEntry(1, "BILLU_1001", 9500),
                    new LeaderboardRankEntry(2, "BILLU_2044", 7200)
                };
                return Task.FromResult(new SharedLeaderboardRemoteResponse(true, playerWeeklyRank: 4, top10: sample));
            }

            public Task<SharedLeaderboardRemoteResponse> SubmitScoreAsync(SharedScoreSubmissionPayload payload)
            {
                SubmitCount++;
                if (SimulateOffline)
                {
                    return Task.FromResult(new SharedLeaderboardRemoteResponse(
                        false,
                        errorMessage: "OFFLINE - SCORE SAVED LOCALLY"));
                }

                IReadOnlyList<LeaderboardRankEntry> sample = new[]
                {
                    new LeaderboardRankEntry(1, "BILLU_1001", 9500),
                    new LeaderboardRankEntry(2, "BILLU_2044", 7200),
                    new LeaderboardRankEntry(4, payload.DisplayName, payload.Score, isCurrentPlayer: true)
                };
                return Task.FromResult(new SharedLeaderboardRemoteResponse(true, playerWeeklyRank: 4, top10: sample));
            }
        }

        private sealed class RecordingPlatformBridge : IGamePlatformBridge
        {
            public string PlatformId => "test";
            public bool IsExternalAudioMuted => false;
            public List<string> Events { get; } = new List<string>();

            public Task InitializeAsync()
            {
                Events.Add("init");
                return Task.CompletedTask;
            }

            public void NotifyLoadingStarted() => Events.Add("loadStart");
            public void NotifyLoadingFinished() => Events.Add("loadFinish");
            public void NotifyGameplayStart() => Events.Add("playStart");
            public void NotifyGameplayStop() => Events.Add("playStop");
        }
    }
}
