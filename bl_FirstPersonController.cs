using UnityEngine;
using System.Collections;
using MFPS.PlayerController;
using UnityEngine.Serialization;
using MFPS.Runtime.Level;
using System;

[RequireComponent(typeof(CharacterController))]
public class bl_FirstPersonController : bl_FirstPersonControllerBase
{
    #region Public members
    [Header("Settings")]
    public float WalkSpeed = 4.5f;
    [FormerlySerializedAs("m_CrouchSpeed")]
    public float runSpeed = 8;
    public float stealthSpeed = 1;
    [FormerlySerializedAs("m_CrouchSpeed")]
    public float crouchSpeed = 2;
    public float slideSpeed = 10;
    [FormerlySerializedAs("m_ClimbSpeed")]
    public float climbSpeed = 1f;
    [FormerlySerializedAs("m_JumpSpeed")]
    public float jumpSpeed;
    public float acceleration = 9;
    public float slopeFriction = 3f;

    public float crouchTransitionSpeed = 0.25f;
    public float crouchHeight = 1.4f;

    public bool canSlide = true;
    [Range(0.2f, 1.5f)] public float slideTime = 0.75f;
    [Range(1, 12)] public float slideFriction = 10;
    [Range(0.1f, 2.5f)] public float slideCoolDown = 1.2f;
    public float slideCameraTiltAngle = -22;

    [Range(0, 2)] public float JumpMinRate = 0.82f;
    float jumpMomentumBooster = 1.5f;
    public float AirAcceleration = 5f;
    [Range(0, 2)] public float AirControlMultiplier = 0.8f;
    public float m_StickToGroundForce;
    public float m_GravityMultiplier;

    private Vector3 currentVelocity = Vector3.zero;

    [LovattoToogle] public bool RunFovEffect = true;
    public float runFOVAmount = 8;
    [LovattoToogle] public bool KeepToCrouch = true;
    public bool canStealthMode = true;
    [Header("Falling")]
    [LovattoToogle] public bool FallDamage = true;
    [Range(0.1f, 5f)]
    public float SafeFallDistance = 3;
    [Range(3, 25)]
    public float DeathFallDistance = 15;
    public PlayerRunToAimBehave runToAimBehave = PlayerRunToAimBehave.StopRunning;

    [Header("Dropping")]
    public float dropControlSpeed = 25;
    public Vector2 dropTiltSpeedRange = new Vector2(20, 60);
    [Header("Mouse Look"), FormerlySerializedAs("m_MouseLook")]
    public MouseLook mouseLook;
    [FormerlySerializedAs("HeatRoot")]
    public Transform headRoot;
    public Transform CameraRoot;
    [Header("HeadBob")]
    [Range(0, 1.2f)] public float headBobMagnitude = 0.9f;
    public float headVerticalBobMagnitude = 0.4f;
    public LerpControlledBob m_JumpBob = new LerpControlledBob();
    [Header("FootSteps")]
    public bl_Footstep footstep;
    public AudioClip jumpSound;           // the sound played when character leaves the ground.
    public AudioClip landSound;           // the sound played when character touches back on ground.
    public AudioClip slideSound;

    [Header("UI")]
    public Sprite StandIcon;
    public Sprite CrouchIcon;
    #endregion

    #region Public properties
    public float RunFov { get; set; } = 0;
    public CollisionFlags m_CollisionFlags { get; set; }
    public override Vector3 Velocity { get; set; }
    public override float VelocityMagnitude { get; set; }
    public override bool isControlable { get; set; } = true;
    #endregion

    #region Private members
    private bool hasPlatformJump = false;
    private float PlatformJumpForce = 0;
    private bool m_Jump, jumpPressed;
    private Vector2 m_Input = new Vector2();
    private Vector3 targetDirection, moveDirection = Vector3.zero;
    private bool m_PreviouslyGrounded = true;
    private bool m_Jumping = false;
    private bool Crounching = false;
    private AudioSource m_AudioSource;
    private bool Finish = false;
    private Vector3 defaultCameraRPosition;
    private bool isClimbing, isAiming = false;
    private bl_Ladder m_Ladder;
    private bool MoveToStarted = false;
#if MFPSM
    private bl_Joystick Joystick;
#endif
    private float PostGroundVerticalPos = 0;
    private bool isFalling = false;
    private int JumpDirection = 0;
    private float HigherPointOnJump;
    private CharacterController m_CharacterController;
    private float lastJumpTime = 0;
    private float WeaponWeight = 1;
    private bool hasTouchGround = false;
    private bool JumpInmune = false;
    private Transform m_Transform;
    private RaycastHit[] SurfaceRay = new RaycastHit[1];
    private Vector3 desiredMove, momentum = Vector3.zero;
    private float VerticalInput, HorizontalInput;
    private bool lastCrouchState = false;
    private float fallingTime = 0;
    private bool haslanding = false;
    private float capsuleRadious;
    private readonly Vector3 feetPositionOffset = new Vector3(0, 0.8f, 0);
    private float slideForce = 0;
    private float lastSlideTime = 0;
    private bl_PlayerReferences playerReferences;
    private Vector3 forwardVector = Vector3.forward;
    private PlayerState lastState = PlayerState.Idle;
    private bool forcedCrouch = false;
    private Vector3 surfaceNormal, surfacePoint = Vector3.zero;
    private float defaultStepOffset = 0.4f;
    private float desiredSpeed = 4;
    private float defaultHeight = 2;
    private bool overrideNextLandEvent = false;
    #endregion

    #region Unity Methods
    /// <summary>
    ///
    /// </summary>
    protected override void Awake()
    {
        if (!photonView.IsMine)
            return;

        base.Awake();
        m_Transform = transform;
        playerReferences = GetComponent<bl_PlayerReferences>();
        m_CharacterController = playerReferences.characterController;
#if MFPSM
        Joystick = FindObjectOfType<bl_Joystick>();
#endif
        defaultCameraRPosition = CameraRoot.localPosition;
        m_AudioSource = gameObject.AddComponent<AudioSource>();
        mouseLook.Init(m_Transform, headRoot);
        lastJumpTime = Time.time;
        defaultStepOffset = m_CharacterController.stepOffset;
        capsuleRadious = m_CharacterController.radius * 0.5f;
        defaultHeight = m_CharacterController.height;
        isControlable = bl_MatchTimeManagerBase.HaveTimeStarted();
    }

    /// <summary>
    ///
    /// </summary>
    protected override void OnEnable()
    {
        bl_EventHandler.onRoundEnd += OnRoundEnd;
        bl_EventHandler.onChangeWeapon += OnChangeWeapon;
        bl_EventHandler.onMatchStart += OnMatchStart;
        bl_EventHandler.onGameSettingsChange += OnGameSettingsChanged;
        bl_EventHandler.onLocalAimChanged += OnAimChange;
#if MFPSM
        bl_TouchHelper.OnCrouch += OnCrouchClicked;
        bl_TouchHelper.OnJump += OnJump;
#endif
    }

    /// <summary>
    ///
    /// </summary>
    protected override void OnDisable()
    {
        bl_EventHandler.onRoundEnd -= OnRoundEnd;
        bl_EventHandler.onChangeWeapon -= OnChangeWeapon;
        bl_EventHandler.onMatchStart -= OnMatchStart;
        bl_EventHandler.onGameSettingsChange -= OnGameSettingsChanged;
        bl_EventHandler.onLocalAimChanged -= OnAimChange;
#if MFPSM
        bl_TouchHelper.OnCrouch -= OnCrouchClicked;
        bl_TouchHelper.OnJump -= OnJump;
#endif
    }

    /// <summary>
    ///
    /// </summary>
    public override void OnUpdate()
    {
        Velocity = m_CharacterController.velocity;
        VelocityMagnitude = Velocity.magnitude;

        if (Finish) return;

        MovementInput();
        GroundDetection();
        CheckStates();
    }

    /// <summary>
    ///
    /// </summary>
    public override void OnLateUpdate()
    {
        UpdateMouseLook();

        if (Finish) return;
        if (m_CharacterController == null || !m_CharacterController.enabled) return;

        moveDirection = Vector3.Lerp(moveDirection, targetDirection, acceleration * Time.deltaTime);

        // apply the movement direction in the character controller
        // apply the movement in LateUpdate to avoid the camera jerky/jitter effect when move and rotate the player camera.
        m_CollisionFlags = m_CharacterController.Move(moveDirection * Time.deltaTime);
    }

    /// <summary>
    ///
    /// </summary>
    public void FixedUpdate()
    {
        if (Finish || m_CharacterController == null || !m_CharacterController.enabled || MoveToStarted)
            return;

        //if player focus is in game
        if (bl_RoomMenu.Instance.isCursorLocked && !bl_GameData.Instance.isChating)
        {
            //determine the player speed
            GetInput(out float s);
            desiredSpeed = Mathf.Lerp(desiredSpeed, s, Time.fixedDeltaTime * 8);
            speed = desiredSpeed;
        }
        else if (State != PlayerState.Sliding)//if player is not focus in game
        {
            m_Input = Vector2.zero;
        }

        if (isClimbing && m_Ladder != null)
        {
            //climbing control
            OnClimbing();
        }
        else
        {
            //player movement
            Move();
        }
    }
    #endregion

    /// <summary>
    /// Triggered when the state of this player controller has changed.
    /// </summary>
    private void OnStateChanged(PlayerState from, PlayerState to)
    {
        if (from == PlayerState.Crouching || to == PlayerState.Crouching)
        {
            DoCrouchTransition();
        }
        else if (from == PlayerState.Sliding || to == PlayerState.Sliding)
        {
            DoCrouchTransition();
        }
        bl_EventHandler.DispatchLocalPlayerStateChange(from, to);
    }

    /// <summary>
    /// Handle the player input key/buttons for the player controller
    /// </summary>
    void MovementInput()
    {
        jumpPressed |= bl_GameInput.Jump();
        if (State == PlayerState.Sliding)
        {
            slideForce -= Time.deltaTime * slideFriction;
            speed = slideForce;
            if (bl_GameInput.Jump())
            {
                State = PlayerState.Jumping;
                m_Jump = true;
            }
            else return;
        }

        if (bl_UtilityHelper.isMobile) return;

        if (!m_Jump && State != PlayerState.Crouching && (Time.time - lastJumpTime) > JumpMinRate)
        {
            m_Jump = bl_GameInput.Jump();
        }

        if (State != PlayerState.Jumping && State != PlayerState.Climbing)
        {
            if (forcedCrouch) return;
            if (KeepToCrouch)
            {
                Crounching = bl_GameInput.Crouch();
                if (Crounching != lastCrouchState)
                {
                    OnCrouchChanged();
                    lastCrouchState = Crounching;
                }
            }
            else
            {
                if (bl_GameInput.Crouch(GameInputType.Down))
                {
                    Crounching = !Crounching;
                    OnCrouchChanged();
                }
            }
        }
    }

    /// <summary>
    /// Check when the player is in a surface (not jumping or falling)
    /// </summary>
    void GroundDetection()
    {
        //if the player has touch the ground after falling
        if (!m_PreviouslyGrounded && m_CharacterController.isGrounded)
        {
            OnLand();
        }
        else if (m_PreviouslyGrounded && !m_CharacterController.isGrounded)//when the player start jumping
        {
            if (!isFalling)
            {
                PostGroundVerticalPos = m_Transform.position.y;
                isFalling = true;
                fallingTime = Time.time;
            }
        }

        if (isFalling)
        {
            VerticalDirectionCheck();
        }

        if (!m_CharacterController.isGrounded && !m_Jumping && m_PreviouslyGrounded)
        {
            targetDirection.y = 0f;
        }

        if (forcedCrouch)
        {
            if ((Time.frameCount % 10) == 0)
            {
                if (!IsHeadHampered())
                {
                    forcedCrouch = false;
                    State = PlayerState.Idle;
                }
            }
        }

        m_PreviouslyGrounded = m_CharacterController.isGrounded;
    }

    /// <summary>
    /// Apply the movement direction to the CharacterController
    /// </summary>
    void Move()
    {
        //if the player is touching the surface
        if (m_CharacterController.isGrounded)
        {
            OnSurface();
            //vertical resistance
            moveDirection.y = -m_StickToGroundForce;
            hasTouchGround = true;
            //has a pending jump
            if (m_Jump || hasPlatformJump)
            {
                DoJump();
            }
        }
        else//if the player is not touching the ground
        {
            //if the player is dropping
            if (State == PlayerState.Dropping)
            {
                //handle the air movement in different process
                OnDropping();
                return;
            }
            else if (State == PlayerState.Gliding)
            {
                //handle the gliding movement in different function
                OnGliding();
                return;
            }

            OnAir();
        }
    }

    /// <summary>
    /// Control the player when is in a surface
    /// </summary>
    void OnSurface()
    {
        // always move along the camera forward as it is the direction that it being aimed at
        // desiredMove = (m_Transform.forward * m_Input.y) + (m_Transform.right * m_Input.x);

        desiredMove.Set(m_Input.x, 0.0f, m_Input.y);
        desiredMove = m_Transform.TransformDirection(desiredMove);

        // get a normal for the surface that is being touched to move along it
        Physics.SphereCastNonAlloc(m_Transform.localPosition, capsuleRadious, Vector3.down, SurfaceRay, m_CharacterController.height * 0.5f, Physics.AllLayers, QueryTriggerInteraction.Ignore);

        //determine the movement angle based in the normal of the current player surface
        desiredMove = Vector3.ProjectOnPlane(desiredMove, SurfaceRay[0].normal);
        targetDirection.x = desiredMove.x * speed;
        targetDirection.z = desiredMove.z * speed;

        SlopeControl();
    }

    /// <summary>
    /// Control the player when is in air (not dropping nor gliding)
    /// </summary>
    void OnAir()
    {
        desiredMove = (m_Transform.forward * Mathf.Clamp01(m_Input.y)) + (m_Transform.right * m_Input.x);

        float wishSpeed = desiredMove.magnitude * speed;
        Vector3 wishDir = desiredMove.normalized;

        float currentSpeed = Vector3.Dot(currentVelocity, wishDir);

        float addSpeed = wishSpeed - currentSpeed;
        if (addSpeed <= 0) {
            return;
        }

        float accelSpeed = AirAcceleration * Time.fixedDeltaTime * wishSpeed;

        if (accelSpeed > addSpeed) {
            accelSpeed = addSpeed;
        }

        currentVelocity += wishDir * accelSpeed;
        currentVelocity += Physics.gravity * m_GravityMultiplier * Time.fixedDeltaTime;

        targetDirection = currentVelocity * Time.fixedDeltaTime;
        targetDirection.x *= AirControlMultiplier;
        targetDirection.z *= AirControlMultiplier;

        m_CollisionFlags = m_CharacterController.Move(targetDirection);
    }
    /// <summary>
    /// Called when the player hit a surface after falling/jumping
    /// </summary>
    void OnLand()
    {
        if (isClimbing) return;

        if (!overrideNextLandEvent)
        {
            //land camera effect
            StartCoroutine(m_JumpBob.DoBobCycle());

            isFalling = false;
            float fallDistance = CalculateFall();
            bl_EventHandler.DispatchPlayerLandEvent(fallDistance);
        }else overrideNextLandEvent = false;

        haslanding = true;
        JumpDirection = 0;
        targetDirection.y = 0f;
        m_Jumping = false;
        m_CharacterController.stepOffset = defaultStepOffset;
        if (State != PlayerState.Crouching)
            State = PlayerState.Idle;

    }

    /// <summary>
    ///
    /// </summary>
    void OnCrouchChanged()
    {
        if (Crounching)
        {
            State = PlayerState.Crouching;
            bl_UIReferences.Instance.PlayerUI.PlayerStateIcon.sprite = CrouchIcon;

            //Slide implementation
            if (canSlide && VelocityMagnitude > WalkSpeed && GetLocalVelocity().z > 0.1f)
            {
                DoSlide();
            }
        }
        else
        {
            if (!IsHeadHampered())
            {
                State = PlayerState.Idle;
                bl_UIReferences.Instance.PlayerUI.PlayerStateIcon.sprite = StandIcon;
            }
            else forcedCrouch = true;
        }
    }

    /// <summary>
    ///
    /// </summary>
    public void DoCrouchTransition()
    {
        StopCoroutine(nameof(CrouchTransition));
        StartCoroutine(nameof(CrouchTransition));
    }

    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    IEnumerator CrouchTransition()
    {
        bool isCrouch = Crounching || State == PlayerState.Sliding;
        float height = isCrouch ? crouchHeight : defaultHeight;
        Vector3 center = new Vector3(0, height * 0.5f, 0);
        Vector3 cameraPosition = CameraRoot.localPosition;
        Vector3 verticalCameraPos = isCrouch ? new Vector3(cameraPosition.x, defaultCameraRPosition.y - (defaultHeight - height), cameraPosition.z) : defaultCameraRPosition;

        float originHeight = m_CharacterController.height;
        Vector3 originCenter = m_CharacterController.center;
        Vector3 originCameraPosition = cameraPosition;

        float d = 0;
        while (d < 1)
        {
            d += Time.deltaTime / crouchTransitionSpeed;
            m_CharacterController.height = Mathf.Lerp(originHeight, height, d);
            m_CharacterController.center = Vector3.Lerp(originCenter, center, d);
            CameraRoot.localPosition = Vector3.Lerp(originCameraPosition, verticalCameraPos, d);
            yield return null;
        }
    }

    /// <summary>
    /// Make the player jump
    /// </summary>
    public override void DoJump()
    {
        momentum = desiredMove * jumpMomentumBooster;
        moveDirection.y = (hasPlatformJump) ? PlatformJumpForce : jumpSpeed;
        targetDirection.y = moveDirection.y;
        PlayJumpSound();
        m_Jump = false;
        m_Jumping = true;
        hasPlatformJump = false;
        if (State == PlayerState.Sliding) mouseLook.SetTiltAngle(0);
        Crounching = false;
        State = PlayerState.Jumping;
        lastJumpTime = Time.time;
        m_CharacterController.stepOffset = 0;
        m_CollisionFlags = m_CharacterController.Move(moveDirection * Time.deltaTime);
    }

    /// <summary>
    /// Make the player slide
    /// </summary>
    public override void DoSlide()
    {
        if ((Time.time - lastSlideTime) < slideTime * slideCoolDown) return;//wait the equivalent of one extra slide before be able to slide again
        if (m_Jumping) return;

        Vector3 startPosition = (m_Transform.position - feetPositionOffset) + (m_Transform.forward * m_CharacterController.radius);
        if (Physics.Linecast(startPosition, startPosition + m_Transform.forward)) return;//there is something in front of the feet's

        State = PlayerState.Sliding;
        slideForce = slideSpeed;//slide force will be continually decreasing
        speed = slideSpeed;
        //playerReferences.gunManager.HeadAnimator.Play("slide-start", 0, 0); // if you want to use an animation instead
        mouseLook.SetTiltAngle(slideCameraTiltAngle);
        if (slideSound != null)
        {
            m_AudioSource.clip = slideSound;
            m_AudioSource.volume = 0.7f;
            m_AudioSource.Play();
        }
        mouseLook.UseOnlyCameraRotation();
        this.InvokeAfter(slideTime, () =>
        {
            if (Crounching && !bl_UtilityHelper.isMobile)
                State = PlayerState.Crouching;
            else if (State != PlayerState.Jumping)
                State = PlayerState.Idle;

            Crounching = false;
            DoCrouchTransition();
            lastSlideTime = Time.time;
            mouseLook.SetTiltAngle(0);
            mouseLook.PortBodyOrientationToCamera();
        });
    }

    /// <summary>
    /// Detect slope limit and apply slide physics.
    /// </summary>
    void SlopeControl()
    {
        float angle = Vector3.Angle(Vector3.up, surfaceNormal);

        if (angle <= m_CharacterController.slopeLimit || angle >= 75) return;

        Vector3 direction = Vector3.up - surfaceNormal * Vector3.Dot(Vector3.up, surfaceNormal);
        float speed = slideSpeed + 1 + Time.deltaTime;

        targetDirection += direction * -speed;
        targetDirection.y = targetDirection.y - surfacePoint.y;
    }

    /// <summary>
    /// Make the player dropping
    /// </summary>
    public override void DoDrop()
    {
        if (isGrounded)
        {
            Debug.Log("Can't drop when player is in a surface");
            return;
        }
        State = PlayerState.Dropping;
    }

    /// <summary>
    /// Called each frame when the player is dropping (fall control is On)
    /// </summary>
    void OnDropping()
    {
        //get the camera upside down angle
        float tilt = Mathf.InverseLerp(0, 90, mouseLook.VerticalAngle);
        //normalize it
        tilt = Mathf.Clamp01(tilt);
        if (mouseLook.VerticalAngle <= 0 || mouseLook.VerticalAngle >= 180) tilt = 0;
        //get the forward direction of the player camera
        desiredMove = headRoot.forward * Mathf.Clamp01(m_Input.y);
        if (desiredMove.y > 0) desiredMove.y = 0;

        //calculate the drop speed based in the upside down camera angle
        float dropSpeed = Mathf.Lerp(m_GravityMultiplier * dropTiltSpeedRange.x, m_GravityMultiplier * dropTiltSpeedRange.y, tilt);
        targetDirection = Physics.gravity * dropSpeed * Time.fixedDeltaTime;
        //if the player press the vertical input -> add velocity in the direction where the camera is looking at
        targetDirection += desiredMove * dropControlSpeed;

        //apply the movement direction in the character controller
        m_CollisionFlags = m_CharacterController.Move(targetDirection * Time.fixedDeltaTime);
    }

    /// <summary>
    /// Make the player glide
    /// </summary>
    public override void DoGliding()
    {
        if (isGrounded)
        {
            Debug.Log("Can't gliding when player is in a surface");
            return;
        }
        State = PlayerState.Gliding;
    }

    /// <summary>
    /// Called each frame when the player is gliding
    /// </summary>
    void OnGliding()
    {
        desiredMove = (m_Transform.forward * m_Input.y) + (m_Transform.right * m_Input.x);
        //how much can the player control the player when is in air.
        float airControlMult = AirControlMultiplier * 5;
        //fall gravity amount
        float gravity = m_GravityMultiplier * 15;

        targetDirection = Physics.gravity * gravity * Time.fixedDeltaTime;
        targetDirection.x = (desiredMove.x * speed) * airControlMult;
        targetDirection.z = (desiredMove.z * speed) * airControlMult;

        m_CollisionFlags = m_CharacterController.Move(targetDirection * Time.fixedDeltaTime);
    }

    /// <summary>
    ///
    /// </summary>
    void OnClimbing()
    {
        if (m_Ladder.Status == bl_Ladder.LadderStatus.Detaching)
        {
            if (!m_Ladder.Exiting)
            {
                m_Ladder.Exiting = true;
                bool wasControllable = isControlable;
                isControlable = false;

                StartCoroutine(MoveTo(m_Ladder.GetNearestExitPosition(m_Transform), () =>
                {
                    SetActiveClimbing(false);
                    m_Ladder.JumpOut();
                    m_Ladder = null;
                    isControlable = true;
                    bl_EventHandler.onPlayerLand(0.5f);
                }));
            }
        }
        else
        {
            desiredMove = m_Ladder.transform.rotation * forwardVector * m_Input.y;
            targetDirection.y = desiredMove.y * climbSpeed;
            targetDirection.x = desiredMove.x * climbSpeed;
            targetDirection.z = desiredMove.z * climbSpeed;

            if (jumpPressed)
            {
                SetActiveClimbing(false);
                m_Ladder.JumpOut();
                m_Ladder = null;

                targetDirection = -m_Transform.forward * 20;
                targetDirection.y = jumpSpeed;
                lastJumpTime = Time.time;
                jumpPressed = false;
                m_Jump = false;
            }
            m_CollisionFlags = m_CharacterController.Move(targetDirection * Time.smoothDeltaTime);

           if(m_Ladder != null) m_Ladder.WatchLimits();
        }
    }

    /// <summary>
    /// Math behind the fall damage calculation
    /// </summary>
    private float CalculateFall()
    {
        float fallDistance = HigherPointOnJump - m_Transform.position.y;
        if (JumpDirection == -1)
        {
            float normalized = PostGroundVerticalPos - m_Transform.position.y;
            fallDistance = Mathf.Abs(normalized);
        }

        if (FallDamage && hasTouchGround && haslanding)
        {
            if (JumpInmune)
            {
                JumpInmune = false;
                return fallDistance;
            }
            if ((Time.time - fallingTime) <= 0.4f)
            {
                bl_EventHandler.DispatchPlayerLandEvent(0.2f);
                return fallDistance;
            }

            float ave = fallDistance / DeathFallDistance;
            if (fallDistance > SafeFallDistance)
            {
                int damage = Mathf.FloorToInt(ave * 100);
                playerReferences.playerHealthManager.DoFallDamage(damage);
            }

            PlayLandingSound(ave);
            fallingTime = Time.time;
        }
        else PlayLandingSound(1);
        return fallDistance;
    }

    /// <summary>
    /// Check in which vertical direction is the player translating to
    /// </summary>
    void VerticalDirectionCheck()
    {
        if (m_Transform.position.y == PostGroundVerticalPos) return;

        //if the direction has not been decided yet
        if (JumpDirection == 0)
        {
            //is the player below or above from the surface he was?
            // 1 = above (jump), -1 = below (falling)
            JumpDirection = (m_Transform.position.y > PostGroundVerticalPos) ? 1 : -1;
        }
        else if (JumpDirection == 1)//if the player jump
        {
            //but not start falling
            if (m_Transform.position.y < PostGroundVerticalPos)
            {
                //get the higher point he reached jumping
                HigherPointOnJump = PostGroundVerticalPos;
            }
            else//if still going up
            {
                PostGroundVerticalPos = m_Transform.position.y;
            }
        }
        else//if the player was falling without jumping
        {

        }
    }

    /// <summary>
    ///
    /// </summary>
    private void GetInput(out float outputSpeed)
    {
        if (!isControlable)
        {
            m_Input = Vector2.zero;
            outputSpeed = 0;
            return;
        }

        // Read input
        HorizontalInput = bl_GameInput.Horizontal;
        VerticalInput = bl_GameInput.Vertical;

#if MFPSM
        if (bl_UtilityHelper.isMobile)
        {
            HorizontalInput = Joystick.Horizontal;
            VerticalInput = Joystick.Vertical;
            VerticalInput = VerticalInput * 1.25f;
        }
#endif
        if (State == PlayerState.Sliding)
        {
            VerticalInput = 1;
            HorizontalInput = 0;
        }

        // m_Input = Vector2.Lerp(m_Input, new Vector2(HorizontalInput, VerticalInput), Time.deltaTime * 25);
        m_Input.Set(HorizontalInput, VerticalInput);

        float inputMagnitude = m_Input.magnitude;
        //if the player is dropping, the speed is calculated in the dropping function
        if (State == PlayerState.Dropping || State == PlayerState.Gliding) { outputSpeed = 0; return; }

        if (State != PlayerState.Climbing && State != PlayerState.Sliding)
        {
            if (inputMagnitude > 0 && State != PlayerState.Crouching)
            {
                if (VelocityMagnitude > 0)
                {
                    float forwardVelocity = GetLocalVelocity().z;
                    if (!bl_UtilityHelper.isMobile)
                    {
                        // On standalone builds, walk/run speed is modified by a key press.
                        // keep track of whether or not the character is walking or running
                        if (bl_GameInput.Run() && forwardVelocity > 0.1f)
                        {
                            if (runToAimBehave == PlayerRunToAimBehave.AimWhileRunning)
                                State = PlayerState.Walking;
                            else if (runToAimBehave == PlayerRunToAimBehave.StopRunning)
                                State = isAiming ? PlayerState.Walking : PlayerState.Walking;
                        }
                        else if (canStealthMode && bl_GameInput.Stealth() && VelocityMagnitude > 0.1f)
                        {
                            State = PlayerState.Stealth;
                        }
                        else
                        {
                            State = PlayerState.Walking;
                        }
                    }
                    else
                    {
                        if (VerticalInput > 1 && forwardVelocity > 0.1f)
                        {
                            State = PlayerState.Walking;
                        }
                        else if (canStealthMode && VerticalInput > 0.05f && VerticalInput <= 0.15f)
                        {
                            State = PlayerState.Stealth;
                        }
                        else
                        {
                            State = PlayerState.Walking;
                        }
                    }
                }
                else
                {
                    if (State != PlayerState.Jumping)
                    {
                        State = PlayerState.Idle;
                    }
                }

            }
            else if (m_CharacterController.isGrounded)
            {
                if (State != PlayerState.Jumping && State != PlayerState.Crouching)
                {
                    State = PlayerState.Idle;
                }
            }
        }

        if (Crounching)
        {
            outputSpeed = (State == PlayerState.Crouching) ? crouchSpeed : runSpeed;
            if (State == PlayerState.Sliding)
            {
                outputSpeed += 1;
            }
        }
        else
        {
            // set the desired speed to be walking or running
            outputSpeed = (State == PlayerState.Running && m_CharacterController.isGrounded) ? runSpeed : WalkSpeed;
            if (State == PlayerState.Stealth)
            {
                outputSpeed = stealthSpeed;
            }
        }
        // normalize input if it exceeds 1 in combined length:
        if (inputMagnitude > 1)
        {
            m_Input.Normalize();
        }

        if (RunFovEffect)
        {
            float rf = (State == PlayerState.Running && m_CharacterController.isGrounded) ? runFOVAmount : 0;
            RunFov = Mathf.Lerp(RunFov, rf, Time.deltaTime * 6);
        }
    }

    /// <summary>
    /// Force the player stop this frame
    /// </summary>
    public override void Stop()
    {
        m_Input = Vector2.zero;
        moveDirection = Vector3.zero;
        desiredMove = Vector3.zero;
        targetDirection = Vector3.zero;
        State = PlayerState.Idle;
    }

    /// <summary>
    ///
    /// </summary>
    public override void PlayFootStepSound()
    {
        if (State == PlayerState.Sliding) return;
        if (!m_CharacterController.isGrounded && !isClimbing)
            return;

        if (!isClimbing)
        {
            if (State == PlayerState.Stealth || State == PlayerState.Crouching)
            {
                footstep?.SetVolumeMuliplier(footstep.stealthModeVolumeMultiplier);
            }
            else footstep?.SetVolumeMuliplier(1f);

            footstep?.DetectAndPlaySurface();
        }
        else
        {
            footstep?.PlayStepForTag("Generic");
        }
    }

    /// <summary>
    ///
    /// </summary>
    public override void PlatformJump(float force)
    {
        hasPlatformJump = true;
        PlatformJumpForce = force;
        JumpInmune = true;
    }

#if MFPSM
    /// <summary>
    ///
    /// </summary>
    void OnCrouchClicked()
    {
        Crounching = !Crounching;
        OnCrouchChanged();
    }

    void OnJump()
    {
        if (!m_Jump && State != PlayerState.Crouching)
        {
            m_Jump = true;
        }
    }
#endif

    /// <summary>
    ///
    /// </summary>
    public override void UpdateMouseLook()
    {
        mouseLook.Update();

        if (bl_GameInput.InputFocus != MFPSInputFocus.Player) return;

        if (!isClimbing)
        {
            mouseLook.UpdateLook(m_Transform, headRoot);
        }
        else
        {
            mouseLook.UpdateLook(m_Transform, headRoot, m_Ladder);
        }
    }

    /// <summary>
    ///
    /// </summary>
    private void CheckStates()
    {
        if (lastState == State) return;
        OnStateChanged(lastState, State);
        lastState = State;
    }

    /// <summary>
    ///
    /// </summary>
    private void PlayLandingSound(float vol = 1)
    {
        vol = Mathf.Clamp(vol, 0.7f, 1);
        m_AudioSource.clip = landSound;
        m_AudioSource.volume = vol;
        m_AudioSource.Play();
    }

    /// <summary>
    ///
    /// </summary>
    private void PlayJumpSound()
    {
        m_AudioSource.volume = 1;
        m_AudioSource.clip = jumpSound;
        m_AudioSource.Play();
    }

    /// <summary>
    /// Check if the player has a obstacle above the head
    /// </summary>
    /// <returns></returns>
    public bool IsHeadHampered()
    {
        Vector3 origin = m_Transform.localPosition + m_CharacterController.center + Vector3.up * m_CharacterController.height * 0.5F;
        float dist = 2.05f - m_CharacterController.height;
        return Physics.Raycast(origin, Vector3.up, dist);
    }

    #region External Events
    void OnRoundEnd()
    {
        Finish = true;
        isControlable = false;
        Stop();
    }

    void OnChangeWeapon(int id)
    {
        isAiming = false;
        var currentWeapon = playerReferences.gunManager.GetCurrentWeapon();
        if (currentWeapon != null)
        {
            WeaponWeight = currentWeapon.WeaponWeight;
        }
        else
        {
            WeaponWeight = 0;
        }
    }
    void OnMatchStart()
    {
        isControlable = true;
    }
    void OnGameSettingsChanged() => mouseLook.FetchSettings();
    void OnAimChange(bool aim)
    {
        isAiming = aim;
        mouseLook.OnAimChange(aim);
    }
    #endregion

    /// <summary>
    ///
    /// </summary>
    private void SetActiveClimbing(bool active)
    {
        isClimbing = active;
        State = (isClimbing) ? PlayerState.Climbing : PlayerState.Idle;
        if (isClimbing) bl_InputInteractionIndicator.ShowIndication(bl_Input.GetButtonName("Jump"), bl_GameTexts.JumpOffLadder);
        else bl_InputInteractionIndicator.SetActiveIfSame(false, bl_GameTexts.JumpOffLadder);
    }

    /// <summary>
    ///
    /// </summary>
    IEnumerator MoveTo(Vector3 position, Action onFinish)
    {
        if (m_Transform == null) m_Transform = transform;

        float t = 0;
        Vector3 from = m_Transform.localPosition;
        m_CharacterController.enabled = false;
        while (t < 1)
        {
            t += Time.deltaTime / 0.5f;
            m_Transform.localPosition = Vector3.Lerp(from, position, t);
            yield return null;
        }
        m_CharacterController.enabled = true;
        onFinish?.Invoke();
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="other"></param>
    private void CheckLadderTrigger(Collider other)
    {
        // if the collider doesn't have a parent, it's not a valid ladder
        if (isClimbing || other.transform.parent == null)
            return;

        var ladder = other.GetComponentInParent<bl_Ladder>();
        if (ladder == null || !ladder.CanUse)
            return;

        // Enter in a ladder trigger

        Stop();
        m_Ladder = ladder;
        // setup the in and out position based on the player height
        ladder.SetUpBounds(playerReferences);

        bool wasControllable = isControlable;
        isControlable = false;
        JumpInmune = true;
        jumpPressed = false;

        SetActiveClimbing(true);

        // get the position to automatically translate the player to start climbing/down-climbing
        Vector3 startPos = ladder.GetAttachPosition(other, m_CharacterController.height);
        overrideNextLandEvent = true;
        // move the player to the start position
        StartCoroutine(MoveTo(startPos, () =>
        {
            // after finish the position adjustment
            isControlable = wasControllable;
            ladder.Status = bl_Ladder.LadderStatus.Climbing;

            // now the movement will be handled in OnClimbing() function
        }));
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="other"></param>
    void OnTriggerEnter(Collider other)
    {
        if (!photonView.IsMine) return;

        CheckLadderTrigger(other);
    }

    /// <summary>
    ///
    /// </summary>
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        surfaceNormal = hit.normal;
        surfacePoint = hit.point;
        /// Enable this if you want player controller apply force on contact to rigidbodys
        /// is commented by default for performance matters.
        /* Rigidbody body = hit.collider.attachedRigidbody;
         //dont move the rigidbody if the character is on top of it
         if (m_CollisionFlags == CollisionFlags.Below)
         {
             return;
         }

         if (body == null || body.isKinematic)
         {
             return;
         }
         body.AddForceAtPosition(m_CharacterController.velocity * 0.1f, hit.point, ForceMode.Impulse);*/
    }

    internal float _speed = 0;
    public float speed
    {
        get
        {
            return _speed;
        }
        set
        {
            _speed = value - WeaponWeight;
            float min = 1.75f;
            if (State == PlayerState.Stealth)
            {
                min = 1;
            }
            _speed = Mathf.Max(_speed, min);
        }
    }

    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    public override float GetCurrentSpeed()
    {
        return speed;
    }

    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    public override float GetSpeedOnState(PlayerState playerState, bool includeModifiers)
    {
        switch (playerState)
        {
            case PlayerState.Walking:
                return includeModifiers ? WalkSpeed - WeaponWeight : WalkSpeed;
            case PlayerState.Running:
                return includeModifiers ? runSpeed - WeaponWeight : runSpeed;
            case PlayerState.Crouching:
                return includeModifiers ? crouchSpeed - WeaponWeight : crouchSpeed;
            case PlayerState.Dropping:
                return dropTiltSpeedRange.y;
        }
        return includeModifiers ? WalkSpeed - WeaponWeight : WalkSpeed;
    }

    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    public override PlayerRunToAimBehave GetRunToAimBehave()
    {
        return runToAimBehave;
    }

    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    public override MouseLookBase GetMouseLook()
    {
        return mouseLook;
    }

    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    public override bl_Footstep GetFootStep()
    {
        return footstep;
    }

    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    public override float GetSprintFov()
    {
        return RunFov;
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="vertical"></param>
    /// <returns></returns>
    public override float GetHeadBobMagnitudes(bool vertical)
    {
        if (vertical) return headVerticalBobMagnitude;
        return headBobMagnitude;
    }

    public Vector3 GetLocalVelocity() => m_Transform.InverseTransformDirection(Velocity);
    public Vector3 MovementDirection => targetDirection;
    public override bool isGrounded { get { return m_CharacterController.isGrounded; } }

    [SerializeField]
    public class LerpControlledBob
    {
        public float BobDuration;
        public float BobAmount;

        private float m_Offset = 0f;


        // provides the offset that can be used
        public float Offset()
        {
            return m_Offset;
        }


        public IEnumerator DoBobCycle()
        {
            // make the camera move down slightly
            float t = 0f;
            while (t < BobDuration)
            {
                m_Offset = Mathf.Lerp(0f, BobAmount, t / BobDuration);
                t += Time.deltaTime;
                yield return new WaitForFixedUpdate();
            }

            // make it move back to neutral
            t = 0f;
            while (t < BobDuration)
            {
                m_Offset = Mathf.Lerp(BobAmount, 0f, t / BobDuration);
                t += Time.deltaTime;
                yield return new WaitForFixedUpdate();
            }
            m_Offset = 0f;
        }
    }
}
