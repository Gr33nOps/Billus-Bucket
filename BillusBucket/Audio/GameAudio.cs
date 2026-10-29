using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Audio;

namespace BillusBucket
{
    /// <summary>
    /// Loads the rooftop background music loop and sound effects,
    /// handling mute toggles, portal mute state, and smooth music volume fades between game states.
    /// </summary>
    internal sealed class GameAudio : IDisposable
    {
        public const string SoundCatch = "catch";
        public const string SoundStart = "start";
        public const string SoundPause = "pause";
        public const string SoundResume = "resume";
        public const string SoundExplode = "explode";
        public const string SoundSad = "sad";
        public const string SoundToggle = "toggle";
        private const string MusicLoopName = "rooftop-loop";

        private const float PlayingMusicVolume = 0.42f;
        private const float MenuMusicVolume = 0.28f;
        private const float GameOverMusicVolume = 0.09f;
        private const float MusicFadeSpeed = 0.8f;

        private const float ExplosionSfxVolume = 0.48f;
        private const float DefaultSfxVolume = 0.34f;
        private const float SadReactionDelaySeconds = 0.45f;

        private static readonly string[] AllSoundNames =
        {
            MusicLoopName,
            SoundCatch,
            SoundStart,
            SoundPause,
            SoundResume,
            SoundExplode,
            SoundSad,
            SoundToggle
        };

        private readonly Dictionary<string, SoundEffect> effects = new Dictionary<string, SoundEffect>();
        private SoundEffectInstance music;
        private float currentMusicVolume;
        private float sadDelay = -1f;
        private bool externallyMuted;
        private bool isDisposed;

        public bool MusicMuted { get; private set; }
        public bool SfxMuted { get; private set; }

        public GameAudio(IAssetStreamProvider assetProvider = null)
        {
            IAssetStreamProvider provider = assetProvider ?? new DesktopAssetStreamProvider();
            try
            {
                foreach (string name in AllSoundNames)
                {
                    using Stream stream = provider.OpenRead("Audio/" + name + ".wav");
                    effects[name] = SoundEffect.FromStream(stream);
                }

                music = effects[MusicLoopName].CreateInstance();
                music.IsLooped = true;
                music.Volume = 0f;
                TryEnsureMusicPlaying();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void NotifyUserInteraction()
        {
            if (isDisposed || MusicMuted || externallyMuted)
            {
                return;
            }

            TryEnsureMusicPlaying();
        }

        public void SetMuted(bool musicMuted, bool sfxMuted)
        {
            bool changed = MusicMuted != musicMuted || SfxMuted != sfxMuted;
            bool wasSfxMuted = SfxMuted;
            if (changed && !wasSfxMuted) Play(SoundToggle);
            MusicMuted = musicMuted;
            SfxMuted = sfxMuted;
            if (changed && wasSfxMuted && !SfxMuted) Play(SoundToggle);

            if (musicMuted && music != null && !isDisposed)
            {
                currentMusicVolume = 0f;
                music.Volume = 0f;
            }
            else if (!musicMuted && !externallyMuted)
            {
                TryEnsureMusicPlaying();
            }
        }

        public void Play(string soundName, float pitch = 0f)
        {
            if (isDisposed || SfxMuted || externallyMuted)
            {
                return;
            }

            if (!effects.TryGetValue(soundName, out SoundEffect effect))
            {
                return;
            }

            float volume = soundName switch
            {
                SoundExplode => ExplosionSfxVolume,
                SoundSad => .20f,
                SoundToggle => .26f,
                SoundCatch => .38f,
                _ => DefaultSfxVolume
            };
            float clampedPitch = Math.Clamp(pitch, -1f, 1f);
            try
            {
                effect.Play(volume, clampedPitch, pan: 0f);
            }
            catch
            {
                // Ignore transient browser WebAudio autoplay rejections prior to user gesture.
            }
        }

        public static float PitchForNote(int value)
        {
            // MonoGame pitch is measured in octaves. Use D, E, F#, G, A, B, D
            // around the pickup's A tone, so adjacent values are audible steps.
            switch (value)
            {
                case 10: return -7f / 12f;
                case 20: return -5f / 12f;
                case 50: return -3f / 12f;
                case 100: return -2f / 12f;
                case 500: return 0f;
                case 1000: return 2f / 12f;
                case 5000: return 5f / 12f;
                default: return 0f;
            }
        }

        public void HitBomb()
        {
            Play(SoundExplode);
            sadDelay = SadReactionDelaySeconds;
        }

        public void Start()
        {
            sadDelay = -1f;
            TryEnsureMusicPlaying();
            Play(SoundStart);
        }

        public void Update(float dt, RoundPhase phase, bool isWindowActive, bool isExternallyMuted = false)
        {
            externallyMuted = isExternallyMuted;
            if (isDisposed || music == null)
            {
                return;
            }

            bool canPlayAudio = isWindowActive && !isExternallyMuted;

            // Count down the post-explosion sad sting only while the window is active,
            // matching the visual explosion timer.
            if (canPlayAudio && sadDelay >= 0f)
            {
                sadDelay -= dt;
                if (sadDelay < 0f && phase == RoundPhase.GameOver)
                {
                    Play(SoundSad);
                }
            }

            float targetVolume = GetTargetMusicVolume(phase, canPlayAudio);
            if (!canPlayAudio)
            {
                currentMusicVolume = 0f;
                music.Volume = 0f;
                return;
            }

            if (targetVolume > 0f && music.State != SoundState.Playing)
            {
                TryEnsureMusicPlaying();
            }

            float maxStep = dt * MusicFadeSpeed;
            currentMusicVolume += Math.Clamp(targetVolume - currentMusicVolume, -maxStep, maxStep);
            music.Volume = Math.Clamp(currentMusicVolume, 0f, 1f);
        }

        private float GetTargetMusicVolume(RoundPhase phase, bool canPlayAudio)
        {
            if (MusicMuted || !canPlayAudio)
            {
                return 0f;
            }

            if (phase == RoundPhase.Playing)
            {
                return PlayingMusicVolume;
            }

            if (phase == RoundPhase.GameOver)
            {
                return GameOverMusicVolume;
            }

            return MenuMusicVolume;
        }

        private void TryEnsureMusicPlaying()
        {
            if (isDisposed || music == null)
            {
                return;
            }

            try
            {
                if (music.State != SoundState.Playing)
                {
                    music.Play();
                }
            }
            catch
            {
                // Browser WebAudio may defer playback until the first user click/key gesture.
            }
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;

            if (music != null)
            {
                music.Stop();
                music.Dispose();
                music = null;
            }

            foreach (SoundEffect sound in effects.Values)
            {
                sound.Dispose();
            }

            effects.Clear();
        }
    }
}
