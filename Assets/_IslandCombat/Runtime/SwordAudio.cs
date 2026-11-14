using UnityEngine;
using UnityEngine.Audio;

namespace IslandCombat
{
    /// <summary>
    /// Audio hooks for the sword. Uses its own AudioSource so its volume and mixer routing are independent
    /// of footsteps and other game audio. Empty clip slots are simply skipped (no placeholder sounds).
    /// </summary>
    [DisallowMultipleComponent]
    public class SwordAudio : MonoBehaviour
    {
        [Tooltip("Optional mixer group (e.g. an SFX/Combat group). Left empty, the source plays to the master output.")]
        public AudioMixerGroup Output;
        [Range(0f, 1f)] public float MasterVolume = 1f;

        [Header("Clips (any number; one is picked at random)")]
        public AudioClip[] DrawClips;
        [Range(0f, 1f)] public float DrawVolume = 0.8f;
        public AudioClip[] SheatheClips;
        [Range(0f, 1f)] public float SheatheVolume = 0.8f;
        public AudioClip[] SwingClips;
        [Range(0f, 1f)] public float SwingVolume = 0.8f;
        public AudioClip[] ImpactClips;
        [Range(0f, 1f)] public float ImpactVolume = 1f;

        [Range(0f, 0.3f)] public float PitchJitter = 0.06f;

        AudioSource _source;

        void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 1f;      // 3D, follows the player
            _source.minDistance = 2f;
            _source.maxDistance = 30f;
            _source.outputAudioMixerGroup = Output;
        }

        public void PlayDraw() => Play(DrawClips, DrawVolume);
        public void PlaySheathe() => Play(SheatheClips, SheatheVolume);
        public void PlaySwing() => Play(SwingClips, SwingVolume);

        /// <summary>Hook for future hit detection. Plays at the contact point when given, else at the player.</summary>
        public void PlayImpact(Vector3? point = null)
        {
            var clip = Pick(ImpactClips);
            if (clip == null) return;
            if (point.HasValue)
            {
                var go = new GameObject("SwordImpactAudio");
                go.transform.position = point.Value;
                var s = go.AddComponent<AudioSource>();
                s.spatialBlend = 1f; s.outputAudioMixerGroup = Output; s.pitch = 1f + Random.Range(-PitchJitter, PitchJitter);
                s.PlayOneShot(clip, ImpactVolume * MasterVolume);
                Destroy(go, clip.length + 0.1f);
            }
            else Play(ImpactClips, ImpactVolume, clip);
        }

        void Play(AudioClip[] clips, float volume, AudioClip chosen = null)
        {
            var clip = chosen != null ? chosen : Pick(clips);
            if (clip == null || _source == null) return;
            _source.pitch = 1f + Random.Range(-PitchJitter, PitchJitter);
            _source.PlayOneShot(clip, volume * MasterVolume);
        }

        static AudioClip Pick(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return null;
            return clips[Random.Range(0, clips.Length)];
        }
    }
}
