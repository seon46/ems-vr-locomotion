using UnityEngine;

/// <summary>
/// 歩行アニメーションの Normalized Time を監視して、踵接地のタイミングで足音を鳴らす。
/// GaitTiming を参照して EMSTrigger と同じ位相定義を共有するので、
/// EMS と足音のタイミングがずれない。
///
/// 使い方:
///  1. アバター（Animator が付いているオブジェクト）にこのスクリプトを付ける
///  2. Footstep Clips に足音の音声ファイルを複数（3〜6個）入れる
///  3. 実行して歩かせ、足が床に着く瞬間と音が合うか確認する
///     ずれていたら Phase Offset で調整する（EMSTrigger の位相も同じだけずれている可能性がある）
/// </summary>
public class FootstepAudio : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private GaitTiming gait;

    [Header("Sound")]
    [Tooltip("足音の音声ファイル。複数入れると毎回ランダムに選ぶ（同じ音の繰り返しで不自然になるのを防ぐ）")]
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] [Range(0f, 1f)] private float volume = 0.6f;
    [Tooltip("1歩ごとに音程を少しばらつかせる幅（0.05 = ±5%）")]
    [SerializeField] [Range(0f, 0.2f)] private float pitchVariation = 0.05f;
    [Tooltip("1歩ごとに音量を少しばらつかせる幅")]
    [SerializeField] [Range(0f, 0.3f)] private float volumeVariation = 0.1f;

    [Header("Timing")]
    [Tooltip("アニメーションの見た目と音がずれる場合の補正。プラスで音が遅れる")]
    [SerializeField] [Range(-0.2f, 0.2f)] private float phaseOffset = 0f;

    [Header("Leading Silence Skip")]
    [Tooltip("音声ファイルの先頭の無音部分を飛ばして再生する")]
    [SerializeField] private bool skipLeadingSilence = true;
    [Tooltip("無音と判定する振幅の閾値")]
    [SerializeField] private float silenceThreshold = 0.01f;

    [Header("Debug")]
    [SerializeField] private bool enableSound = true;
    [SerializeField] private bool logSteps = false;
    [Tooltip("T キーでテスト再生する")]
    [SerializeField] private bool enableTestKey = true;

    private AudioSource leftSource;
    private AudioSource rightSource;
    private float lastPhase = -1f;
    private int lastClipIndex = -1;
    private float[] clipStartTimes;

    void Start()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        if (gait == null)
            gait = GetComponent<GaitTiming>();

        // 左右の足に AudioSource を付ける（3D音にして、足元から聞こえるようにする）
        Transform leftFoot  = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftFoot)  : transform;
        Transform rightFoot = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightFoot) : transform;
        leftSource  = CreateSource(leftFoot  != null ? leftFoot  : transform, "Footstep_L");
        rightSource = CreateSource(rightFoot != null ? rightFoot : transform, "Footstep_R");

        AnalyzeClips();
    }

    /// <summary>
    /// 各クリップの先頭の無音の長さを調べる。
    /// ※ クリップの Load Type が「Decompress On Load」でないと波形を読めない（その場合は 0 秒から再生）
    /// </summary>
    void AnalyzeClips()
    {
        if (footstepClips == null) return;
        clipStartTimes = new float[footstepClips.Length];
        for (int i = 0; i < footstepClips.Length; i++)
        {
            AudioClip c = footstepClips[i];
            if (c == null || !skipLeadingSilence) continue;

            float[] data = new float[c.samples * c.channels];
            if (!c.GetData(data, 0))
            {
                Debug.LogWarning($"[Footstep] {c.name} の波形を読めません。Load Type を Decompress On Load にしてください");
                continue;
            }
            int first = 0;
            while (first < data.Length && Mathf.Abs(data[first]) < silenceThreshold) first++;
            float start = (first / c.channels) / (float)c.frequency;
            // 立ち上がりを削りすぎないよう 5ms 手前から
            clipStartTimes[i] = Mathf.Max(0f, start - 0.005f);
            Debug.Log($"[Footstep] {c.name}: 長さ {c.length:F2}秒 / 先頭の無音 {start * 1000f:F0}ms");
        }
    }

    AudioSource CreateSource(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        AudioSource src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 1f;          // 3D音（足元の方向から聞こえる）
        src.minDistance = 0.5f;
        src.maxDistance = 10f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.dopplerLevel = 0f;
        return src;
    }

    void Update()
    {
        if (enableTestKey && TestKeyPressed())
        {
            Debug.Log("[Footstep] テスト再生（Tキー）");
            PlayStep(rightSource, "TEST");
        }

        if (animator == null || !enableSound) return;

        float phase;
        if (gait != null)
        {
            if (!gait.TryGetWalkPhase(animator, out phase))
            {
                lastPhase = -1f;
                return;
            }
        }
        else
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName("Walking"))
            {
                lastPhase = -1f;
                return;
            }
            phase = Mathf.Repeat(state.normalizedTime, 1f);
        }

        if (lastPhase >= 0f)
        {
            float rightTarget = gait != null ? gait.RightPhase(phaseOffset) : Mathf.Repeat(0.711f + phaseOffset, 1f);
            float leftTarget  = gait != null ? gait.LeftPhase(phaseOffset)  : Mathf.Repeat(0.223f + phaseOffset, 1f);

            if (GaitTiming.Crossed(lastPhase, phase, rightTarget)) PlayStep(rightSource, "R");
            if (GaitTiming.Crossed(lastPhase, phase, leftTarget))  PlayStep(leftSource,  "L");
        }
        lastPhase = phase;
    }

    bool TestKeyPressed()
    {
        return Input.GetKeyDown(KeyCode.T);
    }

    void PlayStep(AudioSource src, string side)
    {
        if (footstepClips == null || footstepClips.Length == 0) return;

        // 直前と同じ音を避けてランダムに選ぶ
        int idx = Random.Range(0, footstepClips.Length);
        if (footstepClips.Length > 1 && idx == lastClipIndex)
            idx = (idx + 1) % footstepClips.Length;
        lastClipIndex = idx;

        src.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        float v = volume * (1f + Random.Range(-volumeVariation, volumeVariation));
        src.volume = Mathf.Clamp01(v);
        src.Stop();
        src.clip = footstepClips[idx];
        // 先頭の無音を飛ばして再生
        src.time = (clipStartTimes != null && idx < clipStartTimes.Length) ? clipStartTimes[idx] : 0f;
        src.Play();

        if (logSteps)
            Debug.Log($"[Footstep] {side} clip={footstepClips[idx].name}");
    }

    /// <summary>実験条件に合わせて足音のON/OFFを切り替える</summary>
    public void SetSoundEnabled(bool enabled)
    {
        enableSound = enabled;
    }
}
