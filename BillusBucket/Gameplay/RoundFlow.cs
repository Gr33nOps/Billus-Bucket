namespace BillusBucket
{
    public enum RoundPhase
    {
        Ready,
        Playing,
        Paused,
        GameOver
    }

    public enum RoundAction
    {
        None,
        StartRun,
        Resume
    }

    public readonly struct ButtonBounds
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public ButtonBounds(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public bool Contains(int x, int y)
        {
            return x >= X && x < X + Width &&
                   y >= Y && y < Y + Height;
        }
    }

    /// <summary>
    /// Controls transitions between Ready, Playing, Paused, GameOver, and the Leaderboard view.
    /// Ensures the leaderboard screen can never open over active or paused gameplay.
    /// </summary>
    public sealed class RoundFlow
    {
        public static readonly ButtonBounds TopRightLeaderboardButton = new ButtonBounds(292, 71, 20, 16);

        private static readonly ButtonBounds CenterMenuButton = new ButtonBounds(126, 85, 68, 18);
        private static readonly ButtonBounds LeaderboardBackButton = new ButtonBounds(130, 147, 60, 18);

        public RoundPhase Phase { get; private set; } = RoundPhase.Ready;
        public bool IsLeaderboardOpen { get; private set; }

        public ButtonBounds MainButton
        {
            get
            {
                if (IsLeaderboardOpen || Phase == RoundPhase.Playing)
                {
                    return default;
                }

                return CenterMenuButton;
            }
        }

        public ButtonBounds LeaderboardButton
        {
            get
            {
                if (IsLeaderboardOpen)
                {
                    return default;
                }

                if (Phase == RoundPhase.Ready || Phase == RoundPhase.GameOver)
                {
                    return TopRightLeaderboardButton;
                }

                return default;
            }
        }

        public ButtonBounds BackButton => IsLeaderboardOpen ? LeaderboardBackButton : default;

        public void OnFocusLost()
        {
            if (Phase == RoundPhase.Playing)
            {
                IsLeaderboardOpen = false;
                Phase = RoundPhase.Paused;
            }
        }

        public RoundAction TryMainActionAt(int x, int y)
        {
            if (!MainButton.Contains(x, y))
            {
                return RoundAction.None;
            }

            return ActivateMainAction();
        }

        public RoundAction ActivateMainAction()
        {
            if (IsLeaderboardOpen)
            {
                return RoundAction.None;
            }

            if (Phase == RoundPhase.Ready || Phase == RoundPhase.GameOver)
            {
                IsLeaderboardOpen = false;
                Phase = RoundPhase.Playing;
                return RoundAction.StartRun;
            }

            if (Phase == RoundPhase.Paused)
            {
                IsLeaderboardOpen = false;
                Phase = RoundPhase.Playing;
                return RoundAction.Resume;
            }

            return RoundAction.None;
        }

        public bool OpenLeaderboard()
        {
            if (Phase != RoundPhase.Ready && Phase != RoundPhase.GameOver)
            {
                return false;
            }

            IsLeaderboardOpen = true;
            return true;
        }

        public bool CloseLeaderboard()
        {
            if (!IsLeaderboardOpen)
            {
                return false;
            }

            IsLeaderboardOpen = false;
            return true;
        }

        public bool TryOpenLeaderboardAt(int x, int y)
        {
            if (IsLeaderboardOpen || !LeaderboardButton.Contains(x, y))
            {
                return false;
            }

            return OpenLeaderboard();
        }

        public bool TryCloseLeaderboardAt(int x, int y)
        {
            if (!IsLeaderboardOpen)
            {
                return false;
            }

            if (!BackButton.Contains(x, y) && !TopRightLeaderboardButton.Contains(x, y))
            {
                return false;
            }

            return CloseLeaderboard();
        }

        public void TogglePause()
        {
            if (Phase == RoundPhase.Playing)
            {
                IsLeaderboardOpen = false;
                Phase = RoundPhase.Paused;
            }
            else if (Phase == RoundPhase.Paused)
            {
                IsLeaderboardOpen = false;
                Phase = RoundPhase.Playing;
            }
        }

        public void HitBomb()
        {
            if (Phase == RoundPhase.Playing)
            {
                IsLeaderboardOpen = false;
                Phase = RoundPhase.GameOver;
            }
        }

        public void ShowReady()
        {
            if (Phase == RoundPhase.GameOver)
            {
                IsLeaderboardOpen = false;
                Phase = RoundPhase.Ready;
            }
        }

        public void Reset()
        {
            IsLeaderboardOpen = false;
            Phase = RoundPhase.Ready;
        }
    }
}
