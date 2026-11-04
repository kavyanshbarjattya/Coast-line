using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Car_controller : MonoBehaviour
{
    [Header("Wheel Colliders")]
    [SerializeField] private WheelCollider FR;
    [SerializeField] private WheelCollider FL;
    [SerializeField] private WheelCollider BR;
    [SerializeField] private WheelCollider BL;

    [Header("Wheel Visuals")]
    [SerializeField] private Transform FR_Trans;
    [SerializeField] private Transform FL_Trans;
    [SerializeField] private Transform BR_Trans;
    [SerializeField] private Transform BL_Trans;

    [Header("Engine - Single Speed Drivetrain")]
    [Tooltip("Engine torque in Nm. Increase only after checking wheel slip.")]
    [SerializeField] private float maxEngineTorque = 450f;
    [SerializeField] private float idleRPM = 850f;
    [SerializeField] private float maxRPM = 5500f;
    [SerializeField] private float maxSpeedKmh = 90f;
    [SerializeField] private float finalDriveRatio = 5.0f;
    [Tooltip("Torque multiplication from the torque-converter simulation at very low speed.")]
    [SerializeField, Range(1f, 3f)] private float lowSpeedTorqueMultiplier = 2.0f;
    [Tooltip("Speed in km/h at which torque-converter multiplication mostly fades out.")]
    [SerializeField] private float converterLockupSpeedKmh = 25f;
    [Tooltip("How quickly engine power builds after pressing the accelerator. Lower values give a heavier, more gradual launch.")]
    [SerializeField, Range(0.2f, 3f)] private float throttleResponse = 0.65f;
    [SerializeField] private float engineRPMResponse = 4f;

    [Header("Drive Mode")]
    [Tooltip("When enabled, the car starts in 4WD. Tab toggles 4WD on/off.")]
    [SerializeField] private bool fourWheelDriveOnStart = false;
    [Tooltip("Front/rear torque split in 4WD. 0.5 means 50% front and 50% rear.")]
    [SerializeField, Range(0.2f, 0.8f)] private float frontTorqueSplit = 0.5f;
    [SerializeField] private KeyCode fourWheelDriveKey = KeyCode.Tab;

    [Header("Traction Control")]
    [SerializeField] private bool tractionControlEnabled = true;
    [SerializeField] private float slipStart = 0.30f;
    [SerializeField] private float slipFullCut = 0.90f;
    [SerializeField, Range(0f, 1f)] private float tractionControlStrength = 0.55f;
    [SerializeField] private float tractionControlResponse = 4f;
    [Tooltip("Allows some wheel slip instead of cutting all power during slow climbing.")]
    [SerializeField, Range(0f, 1f)] private float lowSpeedTractionControlStrength = 0.25f;

    [Header("Brakes")]
    [SerializeField] private float brakeTorque = 4500f;
    [SerializeField, Range(0f, 1f)] private float frontBrakeBias = 0.62f;
    [SerializeField] private float engineBrakingTorque = 90f;

    [Header("Steering")]
    [SerializeField] private float maxSteerAngle = 28f;
    [SerializeField] private float highSpeedSteerAngle = 9f;
    [SerializeField] private float steeringFadeSpeedKmh = 85f;
    [SerializeField] private float steeringResponse = 100f;
    [SerializeField] private float steeringReturnResponse = 130f;

    [Header("Suspension / Anti-Roll")]
    [SerializeField] private float frontAntiRoll = 4500f;
    [SerializeField] private float rearAntiRoll = 4500f;
    [SerializeField] private Vector3 centerOfMass = new Vector3(0f, -0.55f, 0f);

    [Header("Wheel Grip")]
    [Tooltip("WheelCollider friction stiffness multiplier. Tune against the surface.")]
    [SerializeField] private float forwardStiffness = 2.0f;
    [SerializeField] private float sidewaysStiffness = 1.8f;

    [Header("Resistance")]
    [SerializeField] private float rollingResistance = 0.012f;
    [Tooltip("Approximate CdA (drag coefficient multiplied by frontal area).")]
    [SerializeField] private float airDragCoefficientArea = 0.75f;
    [SerializeField] private float airDensity = 1.225f;

    [Header("Steering Wheel Visual")]
    [SerializeField] private Transform steeringWheel;
    [SerializeField] private float steeringWheelRotation = 450f;
    [SerializeField] private float steeringWheelSmoothness = 8f;
    [SerializeField] private Vector3 steeringAxis = Vector3.forward;

    private Rigidbody rb;
    private float horizontalInput;
    private float verticalInput;
    private float smoothedThrottle;
    private float currentSteerAngle;
    private float currentEngineRPM;
    private float currentTractionFactor = 1f;
    private float steeringWheelAngle;
    private float steeringWheelVelocity;
    private bool fourWheelDriveEnabled;

    private Quaternion steeringWheelInitialRotation;

    private const float INPUT_DEADZONE = 0.05f;

    public float SpeedKmh { get; private set; }
    public float EngineRPM => currentEngineRPM;
    public bool FourWheelDriveEnabled => fourWheelDriveEnabled;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMass;

        fourWheelDriveEnabled = fourWheelDriveOnStart;

        if (steeringWheel != null)
            steeringWheelInitialRotation = steeringWheel.localRotation;

        SetWheelFriction(FR);
        SetWheelFriction(FL);
        SetWheelFriction(BR);
        SetWheelFriction(BL);

        currentEngineRPM = idleRPM;

        Debug.Log(
            fourWheelDriveEnabled ? "[Car] 4WD enabled at start." : "[Car] 2WD enabled at start.",
            this
        );
    }

    private void Update()
    {
        ReadInput();
        HandleDriveModeToggle();
        UpdateSteeringWheelVisual();
    }

    private void FixedUpdate()
    {
        UpdateSpeed();
        UpdateEngineRPM();

        ApplyDrive();
        ApplySteering();
        ApplyAntiRoll(FR, FL, frontAntiRoll);
        ApplyAntiRoll(BR, BL, rearAntiRoll);
        ApplyResistance();
    }

    private void LateUpdate()
    {
        UpdateWheelVisual(FR, FR_Trans);
        UpdateWheelVisual(FL, FL_Trans);
        UpdateWheelVisual(BR, BR_Trans);
        UpdateWheelVisual(BL, BL_Trans);
    }

    private void ReadInput()
    {
        horizontalInput = Input.GetAxis("Horizontal");
        verticalInput = Input.GetAxis("Vertical");
    }

    private void HandleDriveModeToggle()
    {
        if (!Input.GetKeyDown(fourWheelDriveKey))
            return;

        fourWheelDriveEnabled = !fourWheelDriveEnabled;

        Debug.Log(
            fourWheelDriveEnabled
                ? "[Car] 4WD ENABLED - power is sent to all four wheels."
                : "[Car] 4WD DISABLED - rear-wheel drive active.",
            this
        );
    }

    private void UpdateSpeed()
    {
        // Signed forward speed is useful for deciding whether opposite input means braking.
        SpeedKmh = Vector3.Dot(rb.linearVelocity, transform.forward) * 3.6f;
    }

    private void UpdateEngineRPM()
    {
        float averageWheelRPM = GetAverageDrivenWheelRPM();

        float wheelBasedRPM =
            averageWheelRPM * finalDriveRatio;

        // A simple torque-converter slip model lets the engine rev while the car
        // is stationary, then couples engine speed to road speed as it moves.
        float converterSlip = 1f - Mathf.InverseLerp(
            0f,
            Mathf.Max(1f, converterLockupSpeedKmh),
            Mathf.Abs(SpeedKmh)
        );

        float throttleRPM = Mathf.Lerp(
            idleRPM,
            maxRPM * 0.82f,
            Mathf.Abs(verticalInput)
        );

        float targetRPM = Mathf.Lerp(
            Mathf.Max(idleRPM, wheelBasedRPM),
            throttleRPM,
            converterSlip * 0.75f
        );

        currentEngineRPM = Mathf.MoveTowards(
            currentEngineRPM,
            Mathf.Clamp(targetRPM, idleRPM, maxRPM),
            engineRPMResponse * maxRPM * Time.fixedDeltaTime
        );
    }

    private float GetAverageDrivenWheelRPM()
    {
        float total = 0f;
        int count = 0;

        if (fourWheelDriveEnabled)
        {
            AddWheelRPM(FR, ref total, ref count);
            AddWheelRPM(FL, ref total, ref count);
        }

        AddWheelRPM(BR, ref total, ref count);
        AddWheelRPM(BL, ref total, ref count);

        return count > 0 ? total / count : 0f;
    }

    private void AddWheelRPM(WheelCollider wheel, ref float total, ref int count)
    {
        if (wheel == null)
            return;

        total += Mathf.Abs(wheel.rpm);
        count++;
    }

    private float GetEngineTorqueAtCurrentRPM()
    {
        float rpm01 = Mathf.InverseLerp(idleRPM, maxRPM, currentEngineRPM);

        // Broad, naturally aspirated-style torque curve:
        // strong midrange, with torque tapering near redline.
        float torqueCurve = 1f - 0.28f * Mathf.Pow((rpm01 - 0.48f) / 0.65f, 2f);
        torqueCurve = Mathf.Clamp(torqueCurve, 0.55f, 1f);

        return maxEngineTorque * torqueCurve;
    }

    private float GetTorqueConverterMultiplier()
    {
        float speed01 = Mathf.InverseLerp(
            0f,
            Mathf.Max(1f, converterLockupSpeedKmh),
            Mathf.Abs(SpeedKmh)
        );

        return Mathf.Lerp(
            lowSpeedTorqueMultiplier,
            1f,
            speed01
        );
    }

    private void ApplyDrive()
    {
        ResetWheelMotorTorques();

        smoothedThrottle = Mathf.MoveTowards(
            smoothedThrottle,
            Mathf.Abs(verticalInput),
            throttleResponse * Time.fixedDeltaTime
        );

        bool movingForward = SpeedKmh > 1.0f;
        bool movingBackward = SpeedKmh < -1.0f;

        bool braking =
            (verticalInput < -INPUT_DEADZONE && movingForward) ||
            (verticalInput > INPUT_DEADZONE && movingBackward);

        if (braking)
        {
            ApplyBrakes(Mathf.Abs(verticalInput) * brakeTorque);
            return;
        }

        if (Mathf.Abs(verticalInput) <= INPUT_DEADZONE)
        {
            ApplyEngineBraking();
            return;
        }

        float speedFactor = 1f - Mathf.InverseLerp(
            maxSpeedKmh * 0.92f,
            maxSpeedKmh,
            Mathf.Abs(SpeedKmh)
        );

        if (verticalInput < 0f)
            speedFactor = 1f - Mathf.InverseLerp(15f, 20f, Mathf.Abs(SpeedKmh));

        float engineTorque = GetEngineTorqueAtCurrentRPM();
        float converterMultiplier = GetTorqueConverterMultiplier();

        // finalDriveRatio is a fixed reduction, not a selectable gear.
        float totalWheelTorque =
            engineTorque *
            finalDriveRatio *
            converterMultiplier *
            smoothedThrottle *
            speedFactor;

        float tractionFactor = CalculateTractionControl();
        totalWheelTorque *= tractionFactor;

        if (verticalInput < 0f)
            totalWheelTorque *= -1f;

        if (fourWheelDriveEnabled)
        {
            float frontTorque = totalWheelTorque * frontTorqueSplit;
            float rearTorque = totalWheelTorque * (1f - frontTorqueSplit);

            ApplyAxleTorque(FL, FR, frontTorque);
            ApplyAxleTorque(BL, BR, rearTorque);
        }
        else
        {
            // Rear-wheel drive when 4WD is switched off.
            ApplyAxleTorque(BL, BR, totalWheelTorque);
        }
    }

    private void ApplyAxleTorque(
        WheelCollider left,
        WheelCollider right,
        float axleTorque)
    {
        // Equal torque split across the two wheels on this axle.
        float perWheelTorque = axleTorque * 0.5f;

        if (left != null)
            left.motorTorque = perWheelTorque;

        if (right != null)
            right.motorTorque = perWheelTorque;
    }

    private void ResetWheelMotorTorques()
    {
        // Reset both torque and brake values every physics step.
        // Otherwise old braking torque can remain applied after releasing the input.
        if (FR != null) { FR.motorTorque = 0f; FR.brakeTorque = 0f; }
        if (FL != null) { FL.motorTorque = 0f; FL.brakeTorque = 0f; }
        if (BR != null) { BR.motorTorque = 0f; BR.brakeTorque = 0f; }
        if (BL != null) { BL.motorTorque = 0f; BL.brakeTorque = 0f; }
    }

    private void ApplyBrakes(float totalBrakeTorque)
    {
        float frontTorque = totalBrakeTorque * frontBrakeBias * 0.5f;
        float rearTorque = totalBrakeTorque * (1f - frontBrakeBias) * 0.5f;

        if (FR != null) FR.brakeTorque = frontTorque;
        if (FL != null) FL.brakeTorque = frontTorque;
        if (BR != null) BR.brakeTorque = rearTorque;
        if (BL != null) BL.brakeTorque = rearTorque;
    }

    private void ApplyEngineBraking()
    {
        float perWheelBrake = engineBrakingTorque * 0.5f;

        if (fourWheelDriveEnabled)
        {
            if (FR != null) FR.brakeTorque = perWheelBrake;
            if (FL != null) FL.brakeTorque = perWheelBrake;
            if (BR != null) BR.brakeTorque = perWheelBrake;
            if (BL != null) BL.brakeTorque = perWheelBrake;
        }
        else
        {
            if (BR != null) BR.brakeTorque = perWheelBrake;
            if (BL != null) BL.brakeTorque = perWheelBrake;
        }
    }

    private float CalculateTractionControl()
    {
        if (!tractionControlEnabled)
        {
            currentTractionFactor = 1f;
            return 1f;
        }

        float maximumSlip = 0f;

        if (fourWheelDriveEnabled)
        {
            maximumSlip = Mathf.Max(maximumSlip, GetWheelForwardSlip(FR));
            maximumSlip = Mathf.Max(maximumSlip, GetWheelForwardSlip(FL));
        }

        maximumSlip = Mathf.Max(maximumSlip, GetWheelForwardSlip(BR));
        maximumSlip = Mathf.Max(maximumSlip, GetWheelForwardSlip(BL));

        float slipAmount = Mathf.InverseLerp(
            slipStart,
            Mathf.Max(slipStart + 0.01f, slipFullCut),
            maximumSlip
        );

        float strength = Mathf.Lerp(
            lowSpeedTractionControlStrength,
            tractionControlStrength,
            Mathf.InverseLerp(8f, 30f, Mathf.Abs(SpeedKmh))
        );

        float targetFactor = 1f - slipAmount * strength;
        targetFactor = Mathf.Clamp(targetFactor, 0.35f, 1f);

        currentTractionFactor = Mathf.MoveTowards(
            currentTractionFactor,
            targetFactor,
            tractionControlResponse * Time.fixedDeltaTime
        );

        return currentTractionFactor;
    }

    private float GetWheelForwardSlip(WheelCollider wheel)
    {
        if (wheel == null || !wheel.GetGroundHit(out WheelHit hit))
            return 0f;

        return Mathf.Abs(hit.forwardSlip);
    }

    private void ApplySteering()
    {
        float speed01 = Mathf.Clamp01(
            Mathf.Abs(SpeedKmh) / Mathf.Max(1f, steeringFadeSpeedKmh)
        );

        float availableSteerAngle = Mathf.Lerp(
            maxSteerAngle,
            highSpeedSteerAngle,
            speed01
        );

        float targetAngle = horizontalInput * availableSteerAngle;

        float response = Mathf.Abs(horizontalInput) > INPUT_DEADZONE
            ? steeringResponse
            : steeringReturnResponse;

        currentSteerAngle = Mathf.MoveTowards(
            currentSteerAngle,
            targetAngle,
            response * Time.fixedDeltaTime
        );

        if (FL != null) FL.steerAngle = currentSteerAngle;
        if (FR != null) FR.steerAngle = currentSteerAngle;
    }

    private void SetWheelFriction(WheelCollider wheel)
    {
        if (wheel == null)
            return;

        WheelFrictionCurve forward = wheel.forwardFriction;
        forward.stiffness = forwardStiffness;
        wheel.forwardFriction = forward;

        WheelFrictionCurve sideways = wheel.sidewaysFriction;
        sideways.stiffness = sidewaysStiffness;
        wheel.sidewaysFriction = sideways;
    }

    private void ApplyAntiRoll(
        WheelCollider left,
        WheelCollider right,
        float antiRollForce)
    {
        if (left == null || right == null)
            return;

        float leftTravel = GetSuspensionTravel(left);
        float rightTravel = GetSuspensionTravel(right);

        float antiRoll = (leftTravel - rightTravel) * antiRollForce;

        if (left.isGrounded)
        {
            rb.AddForceAtPosition(
                left.transform.up * -antiRoll,
                left.transform.position,
                ForceMode.Force
            );
        }

        if (right.isGrounded)
        {
            rb.AddForceAtPosition(
                right.transform.up * antiRoll,
                right.transform.position,
                ForceMode.Force
            );
        }
    }

    private float GetSuspensionTravel(WheelCollider wheel)
    {
        if (!wheel.GetGroundHit(out WheelHit hit))
            return 1f;

        if (wheel.suspensionDistance <= 0.001f)
            return 0f;

        float localY = wheel.transform.InverseTransformPoint(hit.point).y;
        float travel = (-localY - wheel.radius) / wheel.suspensionDistance;

        return Mathf.Clamp01(travel);
    }

    private void ApplyResistance()
    {
        Vector3 planarVelocity = Vector3.ProjectOnPlane(
            rb.linearVelocity,
            Vector3.up
        );

        float speed = planarVelocity.magnitude;

        if (speed < 0.05f)
            return;

        float rollingForce =
            rollingResistance * rb.mass * Physics.gravity.magnitude;

        float aerodynamicDrag =
            0.5f * airDensity * airDragCoefficientArea * speed * speed;

        float totalResistance = rollingForce + aerodynamicDrag;

        // Prevent resistance from reversing the vehicle in one physics step.
        float maximumSafeResistance =
            rb.mass * speed / Mathf.Max(Time.fixedDeltaTime, 0.0001f);

        totalResistance = Mathf.Min(
            totalResistance,
            maximumSafeResistance * 0.8f
        );

        rb.AddForce(
            -planarVelocity.normalized * totalResistance,
            ForceMode.Force
        );
    }

    private void UpdateWheelVisual(
        WheelCollider wheelCollider,
        Transform wheelTransform)
    {
        if (wheelCollider == null || wheelTransform == null)
            return;

        wheelCollider.GetWorldPose(
            out Vector3 position,
            out Quaternion rotation
        );

        wheelTransform.SetPositionAndRotation(position, rotation);
    }

    private void UpdateSteeringWheelVisual()
    {
        if (steeringWheel == null)
            return;

        float normalizedSteering =
            currentSteerAngle / Mathf.Max(0.01f, maxSteerAngle);

        float targetAngle =
            -normalizedSteering * steeringWheelRotation;

        steeringWheelAngle = Mathf.SmoothDamp(
            steeringWheelAngle,
            targetAngle,
            ref steeringWheelVelocity,
            1f / Mathf.Max(steeringWheelSmoothness, 0.01f)
        );

        steeringWheel.localRotation =
            steeringWheelInitialRotation *
            Quaternion.AngleAxis(
                steeringWheelAngle,
                steeringAxis.normalized
            );
    }
}
