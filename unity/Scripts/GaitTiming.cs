using UnityEngine;

/// <summary>
/// 歩行アニメーションの位相（Normalized Time）を管理する共有コンポーネント。
/// EMSTrigger, FootstepAudio, HeadFollow など、
/// 歩行位相に同期する全スクリプトがこのコンポーネントを参照する。
///
/// rightHeelStrike / leftHeelStrike は、GaitDiagnostics で実測した値を入れる。
/// Mixamo "Walking" の場合: 右踵接地 = 0.711, 左踵接地 = 0.223
/// </summary>
public class GaitTiming : MonoBehaviour
{
    [Header("踵接地の位相（GaitDiagnostics の実測値）")]
    [Tooltip("右踵接地の Normalized Time（Mixamo Walking: 0.711）")]
    [Range(0f, 1f)] public float rightHeelStrike = 0.711f;
    [Tooltip("左踵接地の Normalized Time（Mixamo Walking: 0.223）")]
    [Range(0f, 1f)] public float leftHeelStrike = 0.223f;

    [Header("参考（GaitDiagnostics の実測値。計算には使わない）")]
    [Tooltip("歩行1周期の秒数")]
    public float cycleSec = 1.033f;

    [Header("Animator")]
    public string walkingStateName = "Walking";

    public float RightPhase(float offsetFromHeelStrike)
    {
        return Mathf.Repeat(rightHeelStrike + offsetFromHeelStrike, 1f);
    }

    public float LeftPhase(float offsetFromHeelStrike)
    {
        return Mathf.Repeat(leftHeelStrike + offsetFromHeelStrike, 1f);
    }

    /// <summary>
    /// 歩行ステートの位相を取得する。
    /// Idle → Walking の遷移中は「現在のステート」がまだ Idle なので、
    /// 遷移先（次のステート）が Walking ならそちらの位相を使う。
    /// </summary>
    public bool TryGetWalkPhase(Animator anim, out float phase)
    {
        phase = 0f;
        if (anim == null) return false;

        AnimatorStateInfo cur = anim.GetCurrentAnimatorStateInfo(0);
        if (cur.IsName(walkingStateName))
        {
            phase = Mathf.Repeat(cur.normalizedTime, 1f);
            return true;
        }
        if (anim.IsInTransition(0))
        {
            AnimatorStateInfo next = anim.GetNextAnimatorStateInfo(0);
            if (next.IsName(walkingStateName))
            {
                phase = Mathf.Repeat(next.normalizedTime, 1f);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 前フレームの位相 prev から今の位相 now までの間に target を通過したか。
    /// 0→1 のループ境界も対応（例: prev=0.98, now=0.02, target=0.00 → true）。
    /// EMSTrigger, FootstepAudio 等で共通して使う。
    /// </summary>
    public static bool Crossed(float prev, float now, float target)
    {
        target = Mathf.Repeat(target, 1f);
        if (now >= prev)
            return prev < target && target <= now;
        return target > prev || target <= now;
    }
}
