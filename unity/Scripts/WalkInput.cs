using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

public class WalkInput : MonoBehaviour
{
    private Animator animator;
    public float walkSpeed = 1.5f;
    public float turnSpeed = 60f;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        Vector2 leftStick = Vector2.zero;
        Vector2 rightStick = Vector2.zero;

        // Left controller (movement)
        var leftHand = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.LeftHand, leftHand);
        foreach (var device in leftHand)
        {
            Vector2 stick;
            if (device.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick))
            {
                leftStick = stick;
            }
        }

        // Right controller (rotation)
        var rightHand = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, rightHand);
        foreach (var device in rightHand)
        {
            Vector2 stick;
            if (device.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick))
            {
                rightStick = stick;
            }
        }

        // PC fallback
        try
        {
            if (Input.GetKey(KeyCode.W)) leftStick.y = 1f;
            if (Input.GetKey(KeyCode.S)) leftStick.y = -1f;
            if (Input.GetKey(KeyCode.A)) leftStick.x = -1f;
            if (Input.GetKey(KeyCode.D)) leftStick.x = 1f;
            if (Input.GetKey(KeyCode.Q)) rightStick.x = -1f;
            if (Input.GetKey(KeyCode.E)) rightStick.x = 1f;
        }
        catch { }

        bool isWalking = leftStick.magnitude > 0.1f;
        animator.SetBool("isWalking", isWalking);

        if (isWalking)
        {
            Vector3 move = new Vector3(leftStick.x, 0, leftStick.y);
            transform.Translate(move.normalized * walkSpeed * Time.deltaTime);
        }

        if (Mathf.Abs(rightStick.x) > 0.1f)
        {
            transform.Rotate(0, rightStick.x * turnSpeed * Time.deltaTime, 0);
        }
    }
}