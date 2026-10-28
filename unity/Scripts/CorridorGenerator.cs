using UnityEngine;
using UnityEngine.Rendering;

public class CorridorGenerator : MonoBehaviour
{
    public float corridorLength = 100f;
    public float corridorWidth = 4f;
    public float wallHeight = 3f;
    public float pillarSpacing = 5f;

    [Header("Mirror")]
    public float mirrorZ = 5f;
    public float mirrorWidth = 2f;
    public float mirrorHeight = 2.5f;
    public int mirrorResolution = 1024;

    private int mirrorLayer;
    private Renderer rightWallRenderer;

    void Start()
    {
        mirrorLayer = LayerMask.NameToLayer("Mirror");
        if (mirrorLayer < 0)
            Debug.LogWarning("Mirror layer not found. It is optional in this version.");

        BuildCorridor();
        BuildMirror();
    }

    void BuildCorridor()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(corridorWidth, 0.1f, corridorLength);
        floor.transform.position = new Vector3(0, 0, corridorLength / 2f);
        floor.GetComponent<Renderer>().material.color = new Color(0.85f, 0.85f, 0.8f);

        GameObject leftWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leftWall.name = "LeftWall";
        leftWall.transform.localScale = new Vector3(0.2f, wallHeight, corridorLength);
        leftWall.transform.position = new Vector3(-corridorWidth / 2f, wallHeight / 2f, corridorLength / 2f);
        leftWall.GetComponent<Renderer>().material.color = new Color(0.9f, 0.9f, 0.85f);

        GameObject rightWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightWall.name = "RightWall";
        rightWall.transform.localScale = new Vector3(0.2f, wallHeight, corridorLength);
        rightWall.transform.position = new Vector3(corridorWidth / 2f, wallHeight / 2f, corridorLength / 2f);
        rightWallRenderer = rightWall.GetComponent<Renderer>();
        rightWallRenderer.material.color = new Color(0.9f, 0.9f, 0.85f);

        GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ceiling.name = "Ceiling";
        ceiling.transform.localScale = new Vector3(corridorWidth, 0.1f, corridorLength);
        ceiling.transform.position = new Vector3(0, wallHeight, corridorLength / 2f);
        ceiling.GetComponent<Renderer>().material.color = new Color(0.95f, 0.95f, 0.95f);

        for (float z = pillarSpacing; z < corridorLength; z += pillarSpacing)
        {
            GameObject pillarL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillarL.name = "PillarL_" + z;
            pillarL.transform.localScale = new Vector3(0.3f, wallHeight, 0.3f);
            pillarL.transform.position = new Vector3(-corridorWidth / 2f + 0.25f, wallHeight / 2f, z);
            pillarL.GetComponent<Renderer>().material.color = new Color(0.7f, 0.7f, 0.65f);

            GameObject pillarR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillarR.name = "PillarR_" + z;
            pillarR.transform.localScale = new Vector3(0.3f, wallHeight, 0.3f);
            pillarR.transform.position = new Vector3(corridorWidth / 2f - 0.25f, wallHeight / 2f, z);
            pillarR.GetComponent<Renderer>().material.color = new Color(0.7f, 0.7f, 0.65f);
        }

        for (float z = pillarSpacing * 2f; z < corridorLength; z += pillarSpacing * 2f)
        {
            GameObject light = new GameObject("Light_" + z);
            light.transform.position = new Vector3(0, wallHeight - 0.3f, z);
            Light l = light.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = pillarSpacing * 2f;
            l.intensity = 1.5f;
            l.color = new Color(1f, 0.95f, 0.9f);
        }
    }

    void BuildMirror()
    {
        // RightWall centre is +corridorWidth/2 and its thickness is 0.2,
        // so the inner face is x = corridorWidth/2 - 0.1.
        float wallInnerX = corridorWidth / 2f - 0.1f;

        GameObject mirrorPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
        mirrorPlane.name = "MirrorPlane";

        // Put it slightly INSIDE the corridor, in front of the inner wall face.
        mirrorPlane.transform.position = new Vector3(
            wallInnerX - 0.015f,
            mirrorHeight / 2f + 0.3f,
            mirrorZ
        );

        // Explicitly face the Quad toward the corridor centre (-X).
        mirrorPlane.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        mirrorPlane.transform.localScale = new Vector3(mirrorWidth, mirrorHeight, 1f);

        Object.Destroy(mirrorPlane.GetComponent<Collider>());

        if (mirrorLayer >= 0)
            mirrorPlane.layer = mirrorLayer;

        Renderer mirrorRenderer = mirrorPlane.GetComponent<Renderer>();

        // Build a real frame from four thin bars.
        // The previous version used one solid Cube behind/in front of the mirror,
        // which physically covered the entire reflective Quad.
        float frameBorder = 0.06f;
        float frameDepth = 0.03f;
        float mirrorCenterY = mirrorHeight / 2f + 0.3f;

        // Keep the reflective surface a tiny amount in front of the frame
        // toward the corridor (-X), avoiding z-fighting/occlusion.
        float frameX = wallInnerX - frameDepth / 2f;
        float mirrorX = frameX - frameDepth / 2f - 0.002f;
        mirrorPlane.transform.position = new Vector3(mirrorX, mirrorCenterY, mirrorZ);

        Renderer[] frameRenderers = new Renderer[4];

        GameObject frameTop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frameTop.name = "MirrorFrame_Top";
        frameTop.transform.position = new Vector3(
            frameX,
            mirrorCenterY + mirrorHeight / 2f + frameBorder / 2f,
            mirrorZ
        );
        frameTop.transform.localScale = new Vector3(frameDepth, frameBorder, mirrorWidth + frameBorder * 2f);
        frameRenderers[0] = frameTop.GetComponent<Renderer>();

        GameObject frameBottom = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frameBottom.name = "MirrorFrame_Bottom";
        frameBottom.transform.position = new Vector3(
            frameX,
            mirrorCenterY - mirrorHeight / 2f - frameBorder / 2f,
            mirrorZ
        );
        frameBottom.transform.localScale = new Vector3(frameDepth, frameBorder, mirrorWidth + frameBorder * 2f);
        frameRenderers[1] = frameBottom.GetComponent<Renderer>();

        GameObject frameLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frameLeft.name = "MirrorFrame_Left";
        frameLeft.transform.position = new Vector3(
            frameX,
            mirrorCenterY,
            mirrorZ - mirrorWidth / 2f - frameBorder / 2f
        );
        frameLeft.transform.localScale = new Vector3(frameDepth, mirrorHeight, frameBorder);
        frameRenderers[2] = frameLeft.GetComponent<Renderer>();

        GameObject frameRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frameRight.name = "MirrorFrame_Right";
        frameRight.transform.position = new Vector3(
            frameX,
            mirrorCenterY,
            mirrorZ + mirrorWidth / 2f + frameBorder / 2f
        );
        frameRight.transform.localScale = new Vector3(frameDepth, mirrorHeight, frameBorder);
        frameRenderers[3] = frameRight.GetComponent<Renderer>();

        foreach (Renderer r in frameRenderers)
        {
            r.material.color = new Color(0.3f, 0.25f, 0.2f);
            Object.Destroy(r.GetComponent<Collider>());
        }

        int rtHeight = Mathf.Max(64, mirrorResolution);
        int rtWidth = Mathf.Max(64, Mathf.RoundToInt(rtHeight * mirrorWidth / mirrorHeight));

        RenderTexture rt = new RenderTexture(rtWidth, rtHeight, 24, RenderTextureFormat.ARGB32);
        rt.name = "MirrorRT";
        rt.useMipMap = false;
        rt.autoGenerateMips = false;
        rt.Create();

        GameObject camObj = new GameObject("MirrorCamera");
        Camera mirrorCam = camObj.AddComponent<Camera>();
        mirrorCam.targetTexture = rt;
        mirrorCam.depth = -10f;
        mirrorCam.enabled = true;
        mirrorCam.useOcclusionCulling = false;
        mirrorCam.clearFlags = CameraClearFlags.Skybox;

        MirrorReflection reflection = camObj.AddComponent<MirrorReflection>();
        reflection.mirrorPlane = mirrorPlane.transform;
        reflection.nearClip = 0.05f;

        // Critical for this layout: the virtual camera is physically OUTSIDE the
        // right wall. Hide that wall only while the reflection camera renders.
        reflection.hideDuringReflection = new Renderer[]
        {
            rightWallRenderer,
            frameRenderers[0],
            frameRenderers[1],
            frameRenderers[2],
            frameRenderers[3]
        };

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        if (shader == null) shader = Shader.Find("Standard");

        if (shader == null)
        {
            Debug.LogError("No mirror shader found.");
            return;
        }

        Material mirrorMat = new Material(shader);
        mirrorMat.name = "MirrorMaterial_Runtime";

        if (mirrorMat.HasProperty("_BaseMap"))
            mirrorMat.SetTexture("_BaseMap", rt);
        else
            mirrorMat.mainTexture = rt;

        if (mirrorMat.HasProperty("_BaseColor"))
            mirrorMat.SetColor("_BaseColor", Color.white);
        else if (mirrorMat.HasProperty("_Color"))
            mirrorMat.SetColor("_Color", Color.white);

        if (mirrorMat.HasProperty("_Cull"))
            mirrorMat.SetFloat("_Cull", (float)CullMode.Off);

        // Do NOT force-flip UVs here. The off-axis camera basis is constructed
        // to match the Quad's local X/Y directions directly.
        if (mirrorMat.HasProperty("_BaseMap"))
        {
            mirrorMat.SetTextureScale("_BaseMap", Vector2.one);
            mirrorMat.SetTextureOffset("_BaseMap", Vector2.zero);
        }
        else
        {
            mirrorMat.mainTextureScale = Vector2.one;
            mirrorMat.mainTextureOffset = Vector2.zero;
        }

        mirrorRenderer.material = mirrorMat;

        Debug.Log($"Mirror ready: pos={mirrorPlane.transform.position}, forward={mirrorPlane.transform.forward}, RT={rtWidth}x{rtHeight}");
    }
}
