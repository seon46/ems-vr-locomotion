using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class MirrorReflection : MonoBehaviour
{
    [Header("Mirror")]
    public Transform mirrorPlane;

    [Tooltip("Renderers between the virtual camera and the mirror, e.g. the wall holding the mirror.")]
    public Renderer[] hideDuringReflection;

    [Header("Projection")]
    [Min(0.01f)] public float nearClip = 0.05f;

    private Camera mirrorCam;
    private Camera mainCam;
    private Renderer mirrorRenderer;

    private void Awake()
    {
        mirrorCam = GetComponent<Camera>();
        mirrorCam.enabled = true;
        mirrorCam.useOcclusionCulling = false;

        if (mirrorPlane != null)
            mirrorRenderer = mirrorPlane.GetComponent<Renderer>();
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
        RenderPipelineManager.endCameraRendering += EndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= EndCameraRendering;
        RestoreHiddenRenderers();
    }

    private void LateUpdate()
    {
        if (mirrorPlane == null)
            return;

        if (!FindMainCamera())
            return;

        UpdateMirrorCamera();
    }

    private bool FindMainCamera()
    {
        if (mainCam != null && mainCam != mirrorCam && mainCam.isActiveAndEnabled)
            return true;

        mainCam = Camera.main;
        if (mainCam != null && mainCam != mirrorCam && mainCam.isActiveAndEnabled)
            return true;

        foreach (Camera cam in Camera.allCameras)
        {
            if (cam != mirrorCam && cam.isActiveAndEnabled && cam.targetTexture == null)
            {
                mainCam = cam;
                return true;
            }
        }

        mainCam = null;
        return false;
    }

    private void UpdateMirrorCamera()
    {
        Vector3 center = mirrorPlane.position;

        // CorridorGenerator creates the mirror so forward points INTO the corridor.
        Vector3 normalToViewer = mirrorPlane.forward.normalized;

        float viewerDistance = Vector3.Dot(mainCam.transform.position - center, normalToViewer);

        // This mirror is one-sided: the player must be on the corridor side.
        if (viewerDistance <= 0.001f)
        {
            MirrorDebugExtensions.LogWarningOnce("MirrorReflection: player camera is behind the mirror. Check mirrorPlane.forward.");
            return;
        }

        // Virtual eye = real eye reflected across the mirror plane.
        Vector3 virtualEye = mainCam.transform.position - 2f * viewerDistance * normalToViewer;

        // The RenderTexture is the mirror surface itself, so point the virtual camera
        // straight at the mirror and construct an off-axis frustum from its corners.
        Vector3 cameraForward = normalToViewer;
        Vector3 cameraUp = mirrorPlane.up.normalized;

        mirrorCam.transform.SetPositionAndRotation(
            virtualEye,
            Quaternion.LookRotation(cameraForward, cameraUp)
        );

        mirrorCam.clearFlags = mainCam.clearFlags;
        mirrorCam.backgroundColor = mainCam.backgroundColor;
        mirrorCam.allowHDR = mainCam.allowHDR;
        mirrorCam.allowMSAA = mainCam.allowMSAA;
        mirrorCam.farClipPlane = Mathf.Max(mainCam.farClipPlane, 10f);
        mirrorCam.orthographic = false;

        // Render the same scene as the player camera. Mirror recursion is prevented
        // by disabling only the mirror renderer while this camera renders.
        mirrorCam.cullingMask = mainCam.cullingMask;

        BuildOffAxisProjection();
    }

    private void BuildOffAxisProjection()
    {
        Vector3 eye = mirrorCam.transform.position;
        Vector3 camRight = mirrorCam.transform.right;
        Vector3 camUp = mirrorCam.transform.up;
        Vector3 camForward = mirrorCam.transform.forward;

        Vector3 lowerLeft  = mirrorPlane.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 lowerRight = mirrorPlane.TransformPoint(new Vector3( 0.5f, -0.5f, 0f));
        Vector3 upperLeft  = mirrorPlane.TransformPoint(new Vector3(-0.5f,  0.5f, 0f));

        Vector3 va = lowerLeft - eye;
        Vector3 vb = lowerRight - eye;
        Vector3 vc = upperLeft - eye;

        float d = Vector3.Dot(mirrorPlane.position - eye, camForward);
        if (d <= nearClip)
            return;

        float n = nearClip;
        float f = Mathf.Max(mirrorCam.farClipPlane, n + 1f);

        // Project the physical mirror rectangle onto the near plane.
        float left   = Vector3.Dot(va, camRight) * n / d;
        float right  = Vector3.Dot(vb, camRight) * n / d;
        float bottom = Vector3.Dot(va, camUp)    * n / d;
        float top    = Vector3.Dot(vc, camUp)    * n / d;

        if (right <= left || top <= bottom)
        {
            Debug.LogError($"MirrorReflection: invalid frustum L={left} R={right} B={bottom} T={top}");
            return;
        }

        mirrorCam.nearClipPlane = n;
        mirrorCam.projectionMatrix = Matrix4x4.Frustum(left, right, bottom, top, n, f);
    }

    private void BeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam != mirrorCam)
            return;

        if (mirrorRenderer == null && mirrorPlane != null)
            mirrorRenderer = mirrorPlane.GetComponent<Renderer>();

        if (mirrorRenderer != null)
            mirrorRenderer.enabled = false;

        if (hideDuringReflection != null)
        {
            foreach (Renderer r in hideDuringReflection)
            {
                if (r != null)
                    r.enabled = false;
            }
        }
    }

    private void EndCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam == mirrorCam)
            RestoreHiddenRenderers();
    }

    private void RestoreHiddenRenderers()
    {
        if (mirrorRenderer != null)
            mirrorRenderer.enabled = true;

        if (hideDuringReflection != null)
        {
            foreach (Renderer r in hideDuringReflection)
            {
                if (r != null)
                    r.enabled = true;
            }
        }
    }
}

// Unity does not provide Debug.LogWarningOnce, so keep a tiny helper here.
public static class MirrorDebugExtensions
{
    private static readonly System.Collections.Generic.HashSet<string> warnings =
        new System.Collections.Generic.HashSet<string>();

    public static void LogWarningOnce(string message)
    {
        if (warnings.Add(message))
            Debug.LogWarning(message);
    }
}
