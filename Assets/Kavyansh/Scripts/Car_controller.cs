
using UnityEngine;

public class Car_controller : MonoBehaviour
{
    [Header("Wheel Colliders")]
    [SerializeField] private WheelCollider FR;
    [SerializeField] private WheelCollider FL;
    [SerializeField] private WheelCollider BR;
    [SerializeField] private WheelCollider BL;

    [Header("Wheel Transforms")]
    [SerializeField] private Transform FR_Trans;
    [SerializeField] private Transform FL_Trans;
    [SerializeField] private Transform BR_Trans;
    [SerializeField] private Transform BL_Trans;

    [Header("Car Settings")]
    [SerializeField] private float _steerAngle = 25f;
    [SerializeField] private float _motorForce = 1500f;
    [SerializeField] private float _brakeForce = 3000f;

    [Header("Steering Stability")]
    [SerializeField] private float minSteerAtSpeed = 8f;
    [SerializeField] private float maxSteerSpeed = 100f;

    [Header("Anti Roll")]
    [SerializeField] private float frontAntiRoll = 5000f;
    [SerializeField] private float rearAntiRoll = 5000f;

    [Header("Center Of Mass")]
    [SerializeField]
    private Vector3 centerOfMass =
        new Vector3(0f, -0.5f, 0f);

    [Header("Wheel Friction")]
    [SerializeField] private float forwardStiffness = 1.5f;
    [SerializeField] private float sidewaysStiffness = 2f;

    [Header("Rolling Resistance")]
    [SerializeField] private float _rollingResistance = 300f;
    [SerializeField] private float _coastBrakeTorque = 100f;

    private Rigidbody rb;

    private float h, v;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();

        // Lower center of mass
        rb.centerOfMass = centerOfMass;

        // Apply friction
        SetWheelFriction(FR);
        SetWheelFriction(FL);
        SetWheelFriction(BR);
        SetWheelFriction(BL);
    }

    private void FixedUpdate()
    {
        Inputs();
        Drive();
        SteerCar();

        ApplyAntiRoll(FR, FL, frontAntiRoll);
        ApplyAntiRoll(BR, BL, rearAntiRoll);

        UpdateWheelPos(FR, FR_Trans);
        UpdateWheelPos(FL, FL_Trans);
        UpdateWheelPos(BR, BR_Trans);
        UpdateWheelPos(BL, BL_Trans);
    }

    void Inputs()
    {
        h = Input.GetAxis("Horizontal");
        v = Input.GetAxis("Vertical");
    }

    void Drive()
    {
        if (Mathf.Abs(v) > 0.05f)
        {
            BL.motorTorque = v * _motorForce;
            BR.motorTorque = v * _motorForce;

            BL.brakeTorque = 0f;
            BR.brakeTorque = 0f;
        }
        else
        {
            BL.motorTorque = 0f;
            BR.motorTorque = 0f;

            BL.brakeTorque = _coastBrakeTorque;
            BR.brakeTorque = _coastBrakeTorque;

            Vector3 velocity = rb.linearVelocity;

            Vector3 horizontalVelocity =
                Vector3.ProjectOnPlane(velocity, Vector3.up);

            rb.AddForce(
                -horizontalVelocity.normalized * _rollingResistance,
                ForceMode.Force
            );
        }
    }

    void SteerCar()
    {
        float speed = rb.linearVelocity.magnitude * 3.6f;

        float steerMultiplier = Mathf.Lerp(
            1f,
            minSteerAtSpeed / _steerAngle,
            Mathf.InverseLerp(0f, maxSteerSpeed, speed)
        );

        float steer = _steerAngle * h * steerMultiplier;

        FL.steerAngle = steer;
        FR.steerAngle = steer;
    }

    void SetWheelFriction(WheelCollider wheel)
    {
        WheelFrictionCurve forward = wheel.forwardFriction;
        forward.stiffness = forwardStiffness;
        wheel.forwardFriction = forward;

        WheelFrictionCurve sideways = wheel.sidewaysFriction;
        sideways.stiffness = sidewaysStiffness;
        wheel.sidewaysFriction = sideways;
    }

    void ApplyAntiRoll(
        WheelCollider left,
        WheelCollider right,
        float antiRollForce)
    {
        WheelHit hit;

        float travelLeft = 1f;
        float travelRight = 1f;

        bool groundedLeft = left.GetGroundHit(out hit);

        if (groundedLeft)
        {
            travelLeft =
                (-left.transform.InverseTransformPoint(hit.point).y
                - left.radius) / left.suspensionDistance;
        }

        bool groundedRight = right.GetGroundHit(out hit);

        if (groundedRight)
        {
            travelRight =
                (-right.transform.InverseTransformPoint(hit.point).y
                - right.radius) / right.suspensionDistance;
        }

        float antiRoll =
            (travelLeft - travelRight) * antiRollForce;

        if (groundedLeft)
            rb.AddForceAtPosition(
                left.transform.up * -antiRoll,
                left.transform.position
            );

        if (groundedRight)
            rb.AddForceAtPosition(
                right.transform.up * antiRoll,
                right.transform.position
            );
    }

    void UpdateWheelPos(WheelCollider col, Transform t)
    {
        col.GetWorldPose(out Vector3 pos, out Quaternion rot);

        t.position = pos;
        t.rotation = rot;
    }
}
