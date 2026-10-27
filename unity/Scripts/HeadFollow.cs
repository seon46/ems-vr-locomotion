using UnityEngine;

/// <summary>
/// VR カメラをアバターの頭に追従させるスクリプト。
///
/// 目の位置を「首を支点に、HMDの向きに沿って回転させた位置」として計算する。
/// これにより、下を向いたときに目が前方に出て自分の体が見え、
/// 上を向いたときに目が後方に引くリアルな挙動になる。
///
/// 歩行アニメーションに同期した head bob（上下・左右の揺れ）も付与できる。
/// GaitTiming を参照して位相を取得するので、EMSTrigger や FootstepAudio と同期する。
/// </summary>
public class HeadFollow : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private GaitTiming gait;
    [SerializeField] private Transform vrCamera;

    [Header("Eye Offset from Neck Pivot")]
    [Tooltip("首のボーンから目までのローカルオフセット（上12cm、前10cm）")]
    [SerializeField] private Vector3 eyeOffsetFromNeck = new Vector3(0f, 0.12f, 0.10f);

    [Header("Head Bob")]
    [Tooltip("上下の揺れ幅（m）。0 にするとアニメーションの揺れだけ使う")]
    [SerializeField] private float bobVertical = 0f;
    [Tooltip("左右の揺れ幅（m）。歩行位相に同期した体の横揺れ")]
    [SerializeField] private float bobHorizontal = 0.01f;

    [Header("Camera")]
    [Tooltip("Near Clip Plane（体の中が見えないように小さくする）")]
    [SerializeField] private float nearClip = 0.06f;

    [Header("Face Hiding")]
    [Tooltip("顔を非表示にするレイヤー名（カメラの culling mask から除外）")]
    [SerializeField] private string playerHeadLayer = "PlayerHead";

    [Header("Debug")]
    [SerializeField] private bool logPosition = false;

    private Transform neckBone;
    private Transform headBone;
    private Camera cam;
    private int headLayerIndex;

    void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (gait == null) gait = GetComponent<GaitTiming>();

        neckBone = animator != null ? animator.GetBoneTransform(HumanBodyBones.Neck) : null;
        headBone = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;

        if (vrCamera == null)
        {
            cam = Camera.main;
            if (cam != null) vrCamera = cam.transform;
        }
        else
        {
            cam = vrCamera.GetComponent<Camera>();
        }

        if (cam != null)
        {
            cam.nearClipPlane = nearClip;

            // 顔を非表示にする
            headLayerIndex = LayerMask.NameToLayer(playerHeadLayer);
            if (headLayerIndex >= 0)
            {
                cam.cullingMask &= ~(1 << headLayerIndex);
                if (headBone != null) SetLayerRecursive(headBone.gameObject, headLayerIndex);
            }
        }

        if (neckBone == null)
            Debug.LogWarning("[HeadFollow] Neck ボーンが見つかりません。Humanoid アバターが必要です");
    }

    void LateUpdate()
    {
        if (neckBone == null || vrCamera == null) return;

        // 首を支点に、HMDの向きで目の位置を計算
        Vector3 neckPos = neckBone.position;
        Quaternion headRot = vrCamera.rotation;
        Vector3 targetHeadPos = neckPos + headRot * eyeOffsetFromNeck;

        // Head bob（歩行位相に同期した揺れ）
        if (animator != null && gait != null && (bobVertical > 0f || bobHorizontal > 0f))
        {
            float phase;
            if (gait.TryGetWalkPhase(animator, out phase))
            {
                // 右踵接地を基準にした位相で上下・左右を計算
                float phaseFromRight = Mathf.Repeat(phase - gait.rightHeelStrike, 1f);

                // 上下: 1周期に2回（左右の踵接地で最低点）
                if (bobVertical > 0f)
                {
                    float vertical = -Mathf.Abs(Mathf.Sin(phaseFromRight * Mathf.PI * 2f)) * bobVertical;
                    targetHeadPos.y += vertical;
                }

                // 左右: 1周期に1回（体の重心移動）
                if (bobHorizontal > 0f)
                {
                    float lateral = Mathf.Sin(phaseFromRight * Mathf.PI * 2f) * bobHorizontal;
                    targetHeadPos += vrCamera.right * lateral;
                }
            }
        }

        vrCamera.position = targetHeadPos;

        if (logPosition)
            Debug.Log($"[HeadFollow] Neck={neckPos} → Eye={targetHeadPos}");
    }

    void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
