using System;

namespace BillusBucket
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // Prefer compatible Windows OpenAL audio drivers unless the user already configured ALSOFT_DRIVERS.
            if (OperatingSystem.IsWindows() &&
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALSOFT_DRIVERS")))
            {
                Environment.SetEnvironmentVariable("ALSOFT_DRIVERS", "winmm,wasapi");
            }

            string artReviewDirectory = null;
            string liveReviewDirectory = null;

            if (args.Length == 2 && !string.IsNullOrWhiteSpace(args[1]))
            {
                if (args[0] == "--art-review")
                {
                    artReviewDirectory = args[1];
                }
                else if (args[0] == "--live-review")
                {
                    liveReviewDirectory = args[1];
                }
            }

            using BillusBucketGame game = new BillusBucketGame(artReviewDirectory, liveReviewDirectory);
            game.Run();
        }
    }
}
