using UnityEngine;

/// <summary>
/// 歩行アニメーションの踵接地タイミングを実測するための診断ツール。
///
/// 使い方:
///  1. アバターにこのスクリプトを付けて再生する
///  2. 歩行アニメーション中に、右足/左足が床に着く瞬間に
///     R キー（右）/ L キー（左）を押す
///  3. Console に表示される Normalized Time の値を GaitTiming に設定する
///
/// Mixamo "Walking" の実測値: 右踵接地 = 0.711, 左踵接地 = 0.223
/// </summary>
public class GaitDiagnostics : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Settings")]
    [SerializeField] private string walkingStateName = "Walking";

    [Header("Results (実測値)")]
    [SerializeField] private float lastRightHeelStrike;
    [SerializeField] private float lastLeftHeelStrike;
    [SerializeField] private float measuredCycleSec;

    [Header("Debug")]
    [SerializeField] private bool autoMeasureCycle = true;

    private float lastPhase = -1f;
    private float lastWrapTime = -1f;

    void Start()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        Debug.Log("[GaitDiagnostics] 歩行中に R キー（右踵接地）/ L キー（左踵接地）を押して位相を記録してください");
    }

    void Update()
    {
        if (animator == null) return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName(walkingStateName))
        {
            lastPhase = -1f;
            return;
        }

        float phase = Mathf.Repeat(state.normalizedTime, 1f);

        // 周期測定
        if (autoMeasureCycle && lastPhase >= 0f && phase < lastPhase - 0.5f)
        {
            if (lastWrapTime >= 0f)
            {
                measuredCycleSec = Time.time - lastWrapTime;
                Debug.Log($"[GaitDiagnostics] 歩行周期 = {measuredCycleSec:F3} 秒");
            }
            lastWrapTime = Time.time;
        }
        lastPhase = phase;

        // キー入力で踵接地の位相を記録
        if (Input.GetKeyDown(KeyCode.R))
        {
            lastRightHeelStrike = phase;
            Debug.Log($"[GaitDiagnostics] ★ 右踵接地: Normalized Time = {phase:F4}");
        }
        if (Input.GetKeyDown(KeyCode.L))
        {
            lastLeftHeelStrike = phase;
            Debug.Log($"[GaitDiagnostics] ★ 左踵接地: Normalized Time = {phase:F4}");
        }
    }
}
