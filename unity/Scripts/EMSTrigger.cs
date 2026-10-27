using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// 歩行アニメーションの Normalized Time を監視して、
/// Battye et al. (1966) の筋活動タイミングで EMS を発火させる。
///
/// 各筋の求心性収縮区間（踵接地からの割合）:
///   大腿四頭筋   : 踵接地 + 0.08 〜 0.21（膝関節伸展）
///   ハムストリングス: 踵接地 + 0.00 〜 0.24（股関節伸展）
///   前脛骨筋     : 踵接地 + 0.62 〜 0.80（遊脚期の背屈）
///
/// チャンネル割当:
///   #1 右前脛骨筋, #2 右大腿四頭筋, #3 右ハムストリングス
///   #4 左前脛骨筋, #5 左大腿四頭筋, #6 左ハムストリングス
/// </summary>
public class EMSTrigger : MonoBehaviour
{
    [Header("UDP Settings")]
    [SerializeField] private string broadcastIP = "192.168.11.255";
    [SerializeField] private int port = 8266;

    [Header("EMS Levels (0=OFF, 1~15)")]
    [SerializeField] [Range(0, 15)] private int tibialisLevel = 5;
    [SerializeField] [Range(0, 15)] private int quadricepsLevel = 5;
    [SerializeField] [Range(0, 15)] private int hamstringsLevel = 5;

    [Header("発火位置（その脚の踵接地からの周期の割合）")]
    [Tooltip("大腿四頭筋: 踵接地 + 0.08（膝関節パワーが正に変わる点）")]
    [SerializeField] private float quadOnsetFromHeelStrike = 0.08f;
    [Tooltip("ハムストリングス: 踵接地 + 0.00（股関節パワーが正の区間の始まり）")]
    [SerializeField] private float hamOnsetFromHeelStrike  = 0.00f;
    [Tooltip("前脛骨筋: 踵接地 + 0.62（つま先離地後の背屈の始まり）")]
    [SerializeField] private float tibOnsetFromHeelStrike  = 0.62f;

    [Header("実際の発火位相（自動計算・表示のみ）")]
    [SerializeField] private float rQuadPhase;
    [SerializeField] private float rHamPhase;
    [SerializeField] private float rTibPhase;
    [SerializeField] private float lQuadPhase;
    [SerializeField] private float lHamPhase;
    [SerializeField] private float lTibPhase;

    [Header("Stimulation Duration (参考表示: WebUIの刺激時間に入れる値)")]
    [Tooltip("各筋の求心性収縮区間が歩行周期に占める割合")]
    [SerializeField] private float quadFraction = 0.13f;  // 0.08〜0.21
    [SerializeField] private float hamFraction  = 0.24f;  // 0.00〜0.24
    [SerializeField] private float tibFraction  = 0.18f;  // 0.62〜0.80
    [Tooltip("実測した歩行1周期の秒数（実行中に自動更新）")]
    [SerializeField] private float cycleSec;
    [Tooltip("推奨刺激時間(ms) = 周期 × 割合。各台のWebUI『刺激時間』に設定する")]
    [SerializeField] private int quadDurationMs;
    [SerializeField] private int hamDurationMs;
    [SerializeField] private int tibDurationMs;

    [Header("Timing")]
    [Tooltip("同一トリガの再発火防止時間(秒)")]
    [SerializeField] private float cooldown = 0.15f;

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private GaitTiming gait;

    [Header("Debug")]
    [SerializeField] private bool enableEMS = true;
    [SerializeField] private bool logTriggers = true;
    [SerializeField] [Range(0f, 1f)] private float currentPhase;  // Inspector表示用

    private UdpClient udp;
    private IPEndPoint endpoint;
    private float lastPhase = -1f;
    private float lastWrapTime = -1f;

    private float[] lastFireTime;

    private struct TriggerDef
    {
        public string name;
        public int channel;   // 1-based (FIRE パケットの位置)
        public bool right;    // true=右脚, false=左脚
        public System.Func<EMSTrigger, float> getOffset;
        public System.Func<EMSTrigger, int> getLevel;
    }

    private TriggerDef[] triggers;

    void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (gait == null) gait = GetComponent<GaitTiming>();
        if (gait == null)
            Debug.LogError("[EMSTrigger] GaitTiming が見つかりません。アバターに GaitTiming を付けてください");

        udp = new UdpClient();
        udp.EnableBroadcast = true;
        endpoint = new IPEndPoint(IPAddress.Parse(broadcastIP), port);

        triggers = new TriggerDef[]
        {
            new TriggerDef { name = "R Quad (立脚初期 膝伸展)", channel = 2, right = true,
                             getOffset = t => t.quadOnsetFromHeelStrike, getLevel = t => t.quadricepsLevel },
            new TriggerDef { name = "R Ham (立脚初期 股伸展)",  channel = 3, right = true,
                             getOffset = t => t.hamOnsetFromHeelStrike,  getLevel = t => t.hamstringsLevel },
            new TriggerDef { name = "R Tib (遊脚期 背屈)",      channel = 1, right = true,
                             getOffset = t => t.tibOnsetFromHeelStrike,  getLevel = t => t.tibialisLevel },
            new TriggerDef { name = "L Quad (立脚初期 膝伸展)", channel = 5, right = false,
                             getOffset = t => t.quadOnsetFromHeelStrike, getLevel = t => t.quadricepsLevel },
            new TriggerDef { name = "L Ham (立脚初期 股伸展)",  channel = 6, right = false,
                             getOffset = t => t.hamOnsetFromHeelStrike,  getLevel = t => t.hamstringsLevel },
            new TriggerDef { name = "L Tib (遊脚期 背屈)",      channel = 4, right = false,
                             getOffset = t => t.tibOnsetFromHeelStrike,  getLevel = t => t.tibialisLevel },
        };

        lastFireTime = new float[triggers.Length];
        for (int i = 0; i < triggers.Length; i++) lastFireTime[i] = -999f;

        Debug.Log($"[EMSTrigger] 初期化完了 → {broadcastIP}:{port}");
    }

    float TriggerPhase(TriggerDef t)
    {
        float off = t.getOffset(this);
        if (gait == null) return Mathf.Repeat(off + (t.right ? 0f : 0.5f), 1f);
        return t.right ? gait.RightPhase(off) : gait.LeftPhase(off);
    }

    void Update()
    {
        if (animator == null || triggers == null) return;

        // 表示用に実際の発火位相を更新
        rQuadPhase = TriggerPhase(triggers[0]); rHamPhase = TriggerPhase(triggers[1]); rTibPhase = TriggerPhase(triggers[2]);
        lQuadPhase = TriggerPhase(triggers[3]); lHamPhase = TriggerPhase(triggers[4]); lTibPhase = TriggerPhase(triggers[5]);

        string stateName = gait != null ? gait.walkingStateName : "Walking";
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName(stateName)) { lastPhase = -1f; lastWrapTime = -1f; return; }

        float phase = Mathf.Repeat(state.normalizedTime, 1f);
        currentPhase = phase;

        // 位相が1→0に戻った瞬間を周期の区切りとして、歩行1周期の秒数を測る
        if (lastPhase >= 0f && phase < lastPhase - 0.5f)
        {
            if (lastWrapTime >= 0f)
            {
                cycleSec = Time.time - lastWrapTime;
                quadDurationMs = Mathf.RoundToInt(cycleSec * quadFraction * 1000f);
                hamDurationMs  = Mathf.RoundToInt(cycleSec * hamFraction  * 1000f);
                tibDurationMs  = Mathf.RoundToInt(cycleSec * tibFraction  * 1000f);
            }
            lastWrapTime = Time.time;
        }

        // 各トリガ: 前フレームから今フレームの間に発火位相を「通過」したら発火
        // （旧版の ±0.03 の幅判定は、最大 0.03 周期 ≒ 30ms 早く発火することがあった）
        if (lastPhase >= 0f)
        {
            for (int i = 0; i < triggers.Length; i++)
            {
                int level = triggers[i].getLevel(this);
                if (level <= 0) continue;

                float target = TriggerPhase(triggers[i]);
                if (GaitTiming.Crossed(lastPhase, phase, target) && Time.time - lastFireTime[i] > cooldown)
                {
                    lastFireTime[i] = Time.time;
                    FireChannel(triggers[i].channel, level);
                    if (logTriggers)
                        Debug.Log($"[EMS] {triggers[i].name} | Phase={phase:F3} (target {target:F3}) | Level={level}");
                }
            }
        }
        lastPhase = phase;
    }

    /// <summary>指定チャンネルだけにレベルを設定してFIREパケットを送信</summary>
    private void FireChannel(int channel, int level)
    {
        if (!enableEMS) return;

        int[] levels = new int[6];
        levels[channel - 1] = level;

        string msg = "FIRE," + string.Join(",", levels);
        byte[] data = Encoding.UTF8.GetBytes(msg);
        udp.Send(data, data.Length, endpoint);

        if (logTriggers)
            Debug.Log($"[EMS] 送信: {msg}");
    }

    /// <summary>外部から全チャンネル一括発火（テスト用）</summary>
    public void FireAll(int level)
    {
        int[] levels = new int[6];
        for (int i = 0; i < 6; i++) levels[i] = level;
        string msg = "FIRE," + string.Join(",", levels);
        byte[] data = Encoding.UTF8.GetBytes(msg);
        udp.Send(data, data.Length, endpoint);
    }

    /// <summary>EMS のON/OFF切り替え（UIボタン等から呼べる）</summary>
    public void SetEMSEnabled(bool enabled)
    {
        enableEMS = enabled;
        Debug.Log($"[EMSTrigger] EMS {(enabled ? "ON" : "OFF")}");
    }

    void OnDestroy()
    {
        udp?.Close();
    }
}
