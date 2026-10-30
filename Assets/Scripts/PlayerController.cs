using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float maxSpeed = 10f;
    [SerializeField] float acceleration = 80f;
    [SerializeField] float deceleration = 100f;
    [SerializeField, Range(0f, 1f)] float airControl = 0.6f;

    [Header("Jump")]
    [SerializeField] float jumpForce = 16f;
    [SerializeField, Range(0f, 1f)] float jumpCutMultiplier = 0.5f;
    [SerializeField] float fallGravityMultiplier = 1.8f;
    [SerializeField] float coyoteTime = 0.1f;
    [SerializeField] float jumpBufferTime = 0.12f;

    [Header("Dash")]
    [SerializeField] float dashSpeed = 25f;
    [SerializeField] float dashDuration = 0.15f;
    [SerializeField] float dashCooldown = 0.4f;

    [Header("Ground Check")]
    [SerializeField] Transform groundCheck;
    [SerializeField] Vector2 groundCheckSize = new Vector2(0.9f, 0.1f);
    [SerializeField] LayerMask groundLayer;

    Rigidbody2D rb;
    Vector2 move;
    float baseGravity;
    float facing = 1f;

    bool grounded;
    bool coyoteActive;
    bool jumpBuffered;
    bool isDashing;
    bool dashOnCooldown;
    bool hasDash = true;

    Coroutine coyoteRoutine;
    Coroutine bufferRoutine;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        baseGravity = rb.gravityScale;
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
            StartCoroutine(DashRoutine());
    }

    // ---------- Physics ----------

    void FixedUpdate()
    {
        if (isDashing) return;

        UpdateGrounded();
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
            hasDash = true;
            if (!wasGrounded) CancelCoyote();
        }
        else if (wasGrounded && rb.linearVelocity.y <= 0f)
        {
            // walked off a ledge (not jumped) -> grace window
            Restart(ref coyoteRoutine, CoyoteRoutine());
        }
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
        float targetX = move.x * maxSpeed;
        float rate = Mathf.Abs(move.x) > 0.01f ? acceleration : deceleration;
        if (!grounded) rate *= airControl;

        float newX = Mathf.MoveTowards(rb.linearVelocity.x, targetX, rate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);
    }

    void HandleGravity()
    {
        rb.gravityScale = rb.linearVelocity.y < 0f ? baseGravity * fallGravityMultiplier : baseGravity;
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

        Vector2 dir = move.sqrMagnitude > 0.01f ? move.normalized : new Vector2(facing, 0f);

        rb.gravityScale = 0f;
        rb.linearVelocity = dir * dashSpeed;

        yield return new WaitForSeconds(dashDuration);

        rb.gravityScale = baseGravity;
        rb.linearVelocity = dir * maxSpeed; // carry some momentum out of the dash
        isDashing = false;

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