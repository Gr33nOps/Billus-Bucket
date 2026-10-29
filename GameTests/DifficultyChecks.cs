using System;
using System.Globalization;
using System.IO;
using System.Text;
using BillusBucket;

namespace GameTests
{
    internal static class DifficultyChecks
    {
        private const int SeedCount = 100;
        private const int WindowCount = 10;
        private const float WindowSeconds = 30f;
        private const float SessionSeconds = 300f;

        private static void CheckTargetingAndDodgeTime()
        {
            int hazards = 0;
            int nearby = 0;
            for (int seed = 0; seed < 30; seed++)
            {
                var director = new DropDirector(seed);
                float time = 0;
                while (time < 180)
                {
                    var plan = director.Next(time, 66, 252, playerX: 159);
                    if (plan.X < 66 || plan.X > 252)
                        throw new Exception("Targeted drop left the playable area.");
                    if (plan.Kind == DropKind.Bomb)
                    {
                        hazards++;
                        if (Math.Abs(plan.X - 159) <= 48) nearby++;
                    }
                    time += plan.Delay;
                }
            }
            float nearbyFraction = nearby / (float)hazards;
            if (nearbyFraction < .70f || nearbyFraction > .88f)
                throw new Exception("Hazards should pressure Billu while retaining scattered drops.");

            // Reverse from full running speed after a 250ms human reaction delay.
            // Earliest impact: 128 native pixels of travel at the 120px/s speed cap.
            var player = new BucketControl(130, 218, 24, 50);
            player.Update(.25f, 1, null);
            float bombX = player.X;
            player.Update(.25f, 1, null);
            player.Update(128f / 120f - .25f, -1, null);
            if (Math.Abs(player.X - bombX) < 26)
                throw new Exception("Fastest bomb must allow a delayed reversal dodge.");
            Console.WriteLine($"Targeted-spawn bounds and reversal checks passed ({nearbyFraction:P0} nearby hazards).");
        }

        public static void Run()
        {
            CheckTargetingAndDodgeTime();
            // Simulate 100 five-minute runs to verify spawn rates, bomb spacing, and speed caps.
            int[] cashCounts = new int[WindowCount];
            int[] bombCounts = new int[WindowCount];
            double[] bombSpeedSums = new double[WindowCount];
            float[] minBombGaps = new float[WindowCount];
            Array.Fill(minBombGaps, float.MaxValue);

            for (int seed = 0; seed < SeedCount; seed++)
            {
                DropDirector director = new DropDirector(seed);
                float time = 0.25f;
                float lastBombTime = -100f;

                while (time < SessionSeconds)
                {
                    DropPlan drop = director.Next(time, leftBound: 66f, rightBound: 252f);
                    int windowIndex = (int)(time / WindowSeconds);

                    if (drop.Kind == DropKind.Bomb)
                    {
                        float gapSinceLastBomb = time - lastBombTime;
                        if (time < 4.5f || gapSinceLastBomb < 0.619f || drop.Speed > 120.001f)
                        {
                            throw new Exception("Difficulty violated opening grace, hazard spacing, or speed ceiling.");
                        }

                        minBombGaps[windowIndex] = Math.Min(minBombGaps[windowIndex], gapSinceLastBomb);
                        lastBombTime = time;
                        bombCounts[windowIndex]++;
                        bombSpeedSums[windowIndex] += drop.Speed;
                    }
                    else
                    {
                        cashCounts[windowIndex]++;
                    }

                    time += drop.Delay;
                }
            }

            double lateBombsPerWindow = bombCounts[4] / (double)SeedCount;
            if (lateBombsPerWindow < 22.0 || lateBombsPerWindow > 36.0)
            {
                throw new Exception("Later play should provide 22-36 bombs per 30 seconds on average.");
            }

            double lateAverageBombSpeed = bombCounts[4] > 0 ? bombSpeedSums[4] / bombCounts[4] : 0.0;
            if (lateAverageBombSpeed < 100.0)
            {
                throw new Exception("Later bombs should require quicker dodging than the opening.");
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine("seconds,cash_per_30s,bombs_per_30s,average_bomb_speed,min_bomb_gap");

            for (int i = 0; i < WindowCount; i++)
            {
                int startSecond = i * 30;
                int endSecond = (i + 1) * 30;
                double avgCash = cashCounts[i] / (double)SeedCount;
                double avgBombs = bombCounts[i] / (double)SeedCount;
                double avgBombSpeed = bombCounts[i] > 0 ? bombSpeedSums[i] / bombCounts[i] : 0.0;
                float minGap = minBombGaps[i] == float.MaxValue ? 0f : minBombGaps[i];

                report.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}-{1},{2:F2},{3:F2},{4:F2},{5:F2}",
                    startSecond,
                    endSecond,
                    avgCash,
                    avgBombs,
                    avgBombSpeed,
                    minGap));
            }

            string outputPath = Path.Combine(AppContext.BaseDirectory, "difficulty-report.csv");
            File.WriteAllText(outputPath, report.ToString());
            Console.WriteLine("100 seeded five-minute difficulty checks passed. Report: " + outputPath);
        }
    }
}
