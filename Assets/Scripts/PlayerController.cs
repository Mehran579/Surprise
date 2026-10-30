using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float baseSpeed = 10f;
    public float acceleration = 80f;
    public float deceleration = 100f;
    [Range(0f, 1f)] public float airControl = 0.6f;

    [Header("Momentum")]
    public float topSpeed = 20f;
    public float momentumGain = 3f;          // speed added per second while running at full speed
    public float momentumDecay = 8f;         // speed lost per second when you stop or turn around
    [Range(0f, 1f)] public float airDecayMultiplier = 0.25f; // decay is gentler in the air

    [Header("Jump")]
    public float jumpForce = 16f;
    [Range(0f, 1f)] public float jumpCutMultiplier = 0.5f;
    public float fallGravityMultiplier = 1.8f;
    public float coyoteTime = 0.1f;
    public float jumpBufferTime = 0.12f;

    [Header("Dash")]
    public float dashSpeedMultiplier = 2f;     // dash speed = currentMaxSpeed * this
    public float dashDuration = 0.15f;
    public float dashCooldown = 0.4f;
    [Range(0f, 1f)] public float dashMomentumBoost = 0.1f; // fraction of the speed range added when a dash ends

    [Header("Ground Check")]
    public Transform groundCheck;
    public Vector2 groundCheckSize = new Vector2(0.9f, 0.1f);
    public LayerMask groundLayer;

    [Header("Camera Zoom")]
    public CinemachineCamera cam;
    public float maxZoomMultiplier = 1.6f;   // ortho size multiplier at top speed
    public float zoomSmoothing = 4f;         // higher = snappier
    public float zoomStartSpeed = 15f;   // no zoom below this horizontal speed

    float baseOrthoSize;
    float zoomSpeed;

    Rigidbody2D rb;
    Vector2 move;
    Vector2 dashDir;
    float baseGravity;
    float facing = 1f;
    float currentMaxSpeed;

    // speed ring launch state
    float launchDir;
    float launchTimer;

    bool grounded;
    bool coyoteActive;
    bool jumpBuffered;
    bool isDashing;
    bool dashOnCooldown;
    bool hasDash = true;

    Coroutine coyoteRoutine;
    Coroutine bufferRoutine;
    Coroutine dashRoutine;

    // Horizontal input used by movement logic: launch lock wins over the stick
    float MoveX => launchTimer > 0f ? launchDir : move.x;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        baseGravity = rb.gravityScale;
        currentMaxSpeed = baseSpeed;
        if (cam != null) baseOrthoSize = cam.Lens.OrthographicSize;
    }

    // ---------- Input callbacks ----------

    public void OnMove(InputAction.CallbackContext context)
    {
        move = context.ReadValue<Vector2>();
        if (Mathf.Abs(move.x) > 0.01f) facing = Mathf.Sign(move.x);
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (context.started)
        {
            Restart(ref bufferRoutine, JumpBufferRoutine());
        }
        else if (context.canceled && rb.linearVelocity.y > 0f)
        {
            // released early -> short hop
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
        }
    }

    public void OnDash(InputAction.CallbackContext context)
    {
        if (context.started && hasDash && !isDashing && !dashOnCooldown)
            dashRoutine = StartCoroutine(DashRoutine());
    }

    // ---------- Launch API (speed rings) ----------

    public void Launch(float dir, float lockTime, bool resetVertical)
    {
        dir = Mathf.Sign(dir);

        // a ring overrides an in-progress dash
        if (isDashing)
        {
            StopCoroutine(dashRoutine);
            EndDash();
        }

        launchDir = dir;
        launchTimer = lockTime;
        facing = dir;

        currentMaxSpeed = topSpeed;
        rb.linearVelocity = new Vector2(dir * topSpeed, resetVertical ? 0f : rb.linearVelocity.y);
    }

    // ---------- Physics ----------

    void FixedUpdate()
    {
        if (launchTimer > 0f) launchTimer -= Time.fixedDeltaTime;

        UpdateGrounded();

        if (isDashing)
        {
            if (jumpBuffered && (grounded || coyoteActive))
            {
                StopCoroutine(dashRoutine);
                EndDash();
            }
            else return;
        }

        UpdateMomentum();
        HandleJump();
        HandleHorizontal();
        HandleGravity();
    }

    void UpdateGrounded()
    {
        bool wasGrounded = grounded;
        grounded = Physics2D.OverlapBox(groundCheck.position, groundCheckSize, 0f, groundLayer);

        if (grounded)
        {
            if (!isDashing) hasDash = true;
            if (!wasGrounded) CancelCoyote();
        }
        else if (wasGrounded && rb.linearVelocity.y <= 0f)
        {
            // walked off a ledge (not jumped) -> grace window
            Restart(ref coyoteRoutine, CoyoteRoutine());
        }
    }

    void UpdateMomentum()
    {
        float vx = rb.linearVelocity.x;
        bool hasInput = Mathf.Abs(MoveX) > 0.01f;
        bool sameDirection = hasInput && Mathf.Sign(MoveX) == Mathf.Sign(vx);
        bool atCap = Mathf.Abs(vx) >= currentMaxSpeed - 0.5f;

        if (sameDirection && atCap)
        {
            // running flat out in one direction -> build speed
            currentMaxSpeed += momentumGain * Time.fixedDeltaTime;
        }
        else if (hasInput && Mathf.Abs(vx) < 1f)
        {
            // pressing into a wall -> lose everything
            currentMaxSpeed = baseSpeed;
        }
        else if (!sameDirection)
        {
            // stopped or turned around -> bleed speed
            float decay = momentumDecay * (grounded ? 1f : airDecayMultiplier);
            currentMaxSpeed -= decay * Time.fixedDeltaTime;
        }

        currentMaxSpeed = Mathf.Clamp(currentMaxSpeed, baseSpeed, topSpeed);
    }

    void HandleJump()
    {
        if (jumpBuffered && (grounded || coyoteActive))
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            jumpBuffered = false;
            CancelCoyote();
        }
    }

    void HandleHorizontal()
    {
        float targetX = MoveX * currentMaxSpeed;
        float rate = Mathf.Abs(MoveX) > 0.01f ? acceleration : deceleration;
        if (!grounded) rate *= airControl;

        float newX = Mathf.MoveTowards(rb.linearVelocity.x, targetX, rate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);
    }

    void HandleGravity()
    {
        rb.gravityScale = rb.linearVelocity.y < 0f ? baseGravity * fallGravityMultiplier : baseGravity;
    }

    // ---------- Camera ----------
    void Update()
    {
        UpdateCameraZoom();
    }

    void UpdateCameraZoom()
    {
        if (cam == null) return;

        // hold the last value during a dash so the dash spike is ignored
        if (!isDashing)
            zoomSpeed = Mathf.Abs(rb.linearVelocity.x);

        float speedT = Mathf.InverseLerp(zoomStartSpeed, topSpeed, zoomSpeed);
        float targetSize = baseOrthoSize * Mathf.Lerp(1f, maxZoomMultiplier, speedT);

        var lens = cam.Lens;
        lens.OrthographicSize = Mathf.Lerp(lens.OrthographicSize, targetSize, 1f - Mathf.Exp(-zoomSmoothing * Time.deltaTime));
        cam.Lens = lens;
    }

    // ---------- Coroutines ----------

    IEnumerator CoyoteRoutine()
    {
        coyoteActive = true;
        yield return new WaitForSeconds(coyoteTime);
        coyoteActive = false;
    }

    IEnumerator JumpBufferRoutine()
    {
        jumpBuffered = true;
        yield return new WaitForSeconds(jumpBufferTime);
        jumpBuffered = false;
    }

    IEnumerator DashRoutine()
    {
        isDashing = true;
        dashOnCooldown = true;
        hasDash = false;

        dashDir = move.sqrMagnitude > 0.01f ? move.normalized : new Vector2(facing, 0f);

        rb.gravityScale = 0f;
        rb.linearVelocity = dashDir * (currentMaxSpeed * dashSpeedMultiplier);

        yield return new WaitForSeconds(dashDuration);
        EndDash();
    }

    void EndDash()
    {
        if (!isDashing) return;
        isDashing = false;
        rb.gravityScale = baseGravity;

        // dashing feeds momentum, and you exit at the new cap
        currentMaxSpeed = Mathf.Min(topSpeed, currentMaxSpeed + (topSpeed - baseSpeed) * dashMomentumBoost);
        rb.linearVelocity = dashDir * currentMaxSpeed;

        StartCoroutine(DashCooldownRoutine());
    }

    IEnumerator DashCooldownRoutine()
    {
        yield return new WaitForSeconds(dashCooldown);
        dashOnCooldown = false;
    }

    // ---------- Helpers ----------

    void Restart(ref Coroutine handle, IEnumerator routine)
    {
        if (handle != null) StopCoroutine(handle);
        handle = StartCoroutine(routine);
    }

    void CancelCoyote()
    {
        if (coyoteRoutine != null) StopCoroutine(coyoteRoutine);
        coyoteActive = false;
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(groundCheck.position, groundCheckSize);
    }
}