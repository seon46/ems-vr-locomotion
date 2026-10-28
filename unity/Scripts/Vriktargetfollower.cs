using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

public class VRIKTargetFollower : MonoBehaviour
{
    public Transform xrOrigin;
    public Transform leftHandTarget;
    public Transform rightHandTarget;

    [Header("Position Offset")]
    public Vector3 leftHandPosOffset = new Vector3(0, 0, 0);
    public Vector3 rightHandPosOffset = new Vector3(0, 0, 0);

    [Header("Rotation Offset")]
    public Vector3 leftHandRotOffset = new Vector3(0, 0, 0);
    public Vector3 rightHandRotOffset = new Vector3(0, 0, 0);

    void Update()
    {
        UpdateHand(XRNode.LeftHand, leftHandTarget, leftHandPosOffset, leftHandRotOffset);
        UpdateHand(XRNode.RightHand, rightHandTarget, rightHandPosOffset, rightHandRotOffset);
    }

    void UpdateHand(XRNode node, Transform target, Vector3 posOffset, Vector3 rotOffset)
    {
        if (target == null) return;

        var devices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(node, devices);

        foreach (var device in devices)
        {
            Vector3 pos;
            Quaternion rot;

            if (device.TryGetFeatureValue(CommonUsages.devicePosition, out pos))
            {
                if (xrOrigin != null)
                    target.position = xrOrigin.TransformPoint(pos) + posOffset;
                else
                    target.position = pos + posOffset;
            }
            if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out rot))
            {
                if (xrOrigin != null)
                    target.rotation = xrOrigin.rotation * rot * Quaternion.Euler(rotOffset);
                else
                    target.rotation = rot * Quaternion.Euler(rotOffset);
            }
        }
    }
}