using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class OrderBookWindowController : MonoBehaviour
{
    [Header("References")]
    public OrderBookSourceBehaviour source;
    public OrderBookHistoryRenderer renderer;

    [Header("Window")]
    [Min(1)] public int startIndex = 0;
    [Min(1)] public int windowSize = 300;

    [Header("Startup")]
    public bool refreshSourceOnStart = true;
    public bool loadWindowOnStart = true;

    [Header("Input System")]
    [Tooltip("Bind this to an Input System action, e.g. a Value/Vector2 action bound to <Mouse>/scroll.")]
    public InputActionReference scrollAction;

    [Tooltip("If true, interpret positive wheel input as scrolling backward in history.")]
    public bool invertScrollDirection = false;

    [Min(1)]
    [Tooltip("How many snapshots to move per wheel notch.")]
    public int scrollStep = 10;

    [Min(0.001f)]
    [Tooltip("Ignore tiny scroll values below this threshold.")]
    public float scrollDeadzone = 0.01f;

    [Header("Live Mode")]
    public bool followTailIfLive = false;

    private readonly List<OrderBookSnapshot> _buffer = new List<OrderBookSnapshot>();
    private float _pendingScrollY = 0f;

    private void OnEnable()
    {
        EnableScrollAction();
    }

    private void OnDisable()
    {
        DisableScrollAction();
    }

    private void Start()
    {
        if (source == null || renderer == null)
            return;

        if (refreshSourceOnStart)
            source.RefreshSource();

        if (loadWindowOnStart)
            RefreshWindow();
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        if (source == null || renderer == null)
            return;

        if (followTailIfLive && source.IsLive)
        {
            int tailStart = Mathf.Max(0, source.Count - windowSize);
            if (tailStart != startIndex)
            {
                startIndex = tailStart;
                RefreshWindow();
            }
        }

        if (Mathf.Abs(_pendingScrollY) > scrollDeadzone)
        {
            float scrollY = _pendingScrollY;
            _pendingScrollY = 0f;

            int direction = scrollY > 0f ? -1 : 1;
            if (invertScrollDirection)
                direction *= -1;

            ScrollBy(direction * scrollStep);
        }
    }

    [ContextMenu("Refresh Window")]
    public void RefreshWindow()
    {
        if (source == null || renderer == null)
            return;

        if (source.Count <= 0)
        {
            renderer.ClearWindow();
            return;
        }

        startIndex = Mathf.Clamp(startIndex, 0, Mathf.Max(0, source.Count - 1));

        _buffer.Clear();
        bool gotAny = source.TryGetRange(startIndex, windowSize, _buffer);

        if (!gotAny || _buffer.Count == 0)
            renderer.ClearWindow();
        else
            renderer.SetWindow(_buffer);
    }

    public void ScrollBy(int delta)
    {
        if (source == null)
            return;

        int maxStart = Mathf.Max(0, source.Count - 1);
        startIndex = Mathf.Clamp(startIndex + delta, 0, maxStart);
        RefreshWindow();
    }

    public void JumpToIndex(int index)
    {
        if (source == null)
            return;

        int maxStart = Mathf.Max(0, source.Count - 1);
        startIndex = Mathf.Clamp(index, 0, maxStart);
        RefreshWindow();
    }

    public void JumpToTime(long capturedAtMs)
    {
        if (source == null)
            return;

        int index = source.FindNearestIndexByTime(capturedAtMs);
        if (index < 0)
            return;

        startIndex = index;
        RefreshWindow();
    }

    [ContextMenu("Jump To Start")]
    public void JumpToStart()
    {
        JumpToIndex(0);
    }

    [ContextMenu("Jump To End")]
    public void JumpToEnd()
    {
        if (source == null)
            return;

        JumpToIndex(Mathf.Max(0, source.Count - windowSize));
    }

    private void EnableScrollAction()
    {
        if (scrollAction == null || scrollAction.action == null)
            return;

        scrollAction.action.performed -= OnScrollPerformed;
        scrollAction.action.performed += OnScrollPerformed;
        scrollAction.action.Enable();
    }

    private void DisableScrollAction()
    {
        if (scrollAction == null || scrollAction.action == null)
            return;

        scrollAction.action.performed -= OnScrollPerformed;
        scrollAction.action.Disable();
    }

    private void OnScrollPerformed(InputAction.CallbackContext context)
    {
        Vector2 scroll = context.ReadValue<Vector2>();
        _pendingScrollY += scroll.y;
    }
}
