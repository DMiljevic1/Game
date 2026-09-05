using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's single point of contact with the world: looks down the camera,
/// picks the one thing in reach, lists what can be done to it, and routes the key.
///
/// Key bindings live on the interactables (a door says E, a pickup says F), so a new
/// object cannot silently steal a key that something else already uses.
///
/// Co-op note: this is per-player and local. It only ever asks an interactable to
/// act; the interactable owns whatever shared state changes as a result.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInteractor : MonoBehaviour
{
    [Header("Reach")]
    public Camera viewCamera;
    public float range = 3.5f;

    [Header("Keys")]
    [Tooltip("Drops whatever is being carried. Every other key is declared by the interactable itself.")]
    public KeyCode dropKey = KeyCode.Q;

    [Header("Carrying")]
    [Tooltip("Where a carried item is parented. Usually a child of the camera.")]
    public Transform carrySocket;

    private readonly List<InteractionOption> options = new List<InteractionOption>();
    private Carryable carried;
    private IInteractable target;

    public Carryable Carried { get { return carried; } }
    public bool IsCarrying { get { return carried != null; } }

    /// <summary>The carried item as T, or null. Lets interactables ask "holding a fuel can?".</summary>
    public T GetCarried<T>() where T : Carryable
    {
        return carried as T;
    }

    void Start()
    {
        if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
        if (viewCamera == null)
        {
            Debug.LogError("PlayerInteractor on " + name + " has no camera; interaction will not work.", this);
        }
        if (carrySocket == null)
        {
            Debug.LogError("PlayerInteractor on " + name + " has no carry socket; items cannot be held.", this);
        }
    }

    void Update()
    {
        AcquireTarget();

        if (target != null)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (Input.GetKeyDown(options[i].key))
                {
                    target.Interact(this, options[i].key);
                    break;
                }
            }
        }

        if (carried != null && Input.GetKeyDown(dropKey))
        {
            DropCarried();
        }
    }

    private void AcquireTarget()
    {
        target = null;
        options.Clear();

        if (viewCamera == null) return;

        Ray ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, range, ~0, QueryTriggerInteraction.Collide)) return;

        // Never target ourselves or whatever we are already holding.
        if (hit.transform.root == transform.root) return;

        IInteractable candidate = hit.collider.GetComponentInParent<IInteractable>();
        if (candidate == null) return;

        candidate.GetOptions(this, options);
        if (options.Count == 0) return;

        target = candidate;
    }

    /// <summary>Take an item into the hands. Anything already held is dropped first.</summary>
    public void Carry(Carryable item)
    {
        if (item == null || carrySocket == null) return;
        if (carried != null) DropCarried();

        carried = item;
        item.OnPickedUp(carrySocket);
    }

    public void DropCarried()
    {
        if (carried == null) return;

        Carryable item = carried;
        carried = null;

        // Put it down in front of the player, resting on whatever is below.
        Vector3 ahead = transform.position + transform.forward * 1.2f + Vector3.up * 1.0f;
        RaycastHit hit;
        Vector3 place = Physics.Raycast(ahead, Vector3.down, out hit, 4f)
                      ? hit.point
                      : transform.position + transform.forward * 1.2f;

        item.OnDropped(place, transform.rotation);
    }

    /// <summary>Remove the carried item from the hands without placing it (it was consumed).</summary>
    public void ConsumeCarried()
    {
        carried = null;
    }

    void OnGUI()
    {
        // Crosshair, so the player knows the interaction is aim-based.
        Hud.Label(new Rect(0f, Screen.height * 0.5f - Hud.Prompt.fontSize * 0.5f, Screen.width, Hud.Prompt.fontSize),
                  "+", Hud.Centered, new Color(1f, 1f, 1f, 0.75f));

        // Every available action, one per line, each showing its own key.
        for (int i = 0; i < options.Count; i++)
        {
            float y = Screen.height * 0.5f + Hud.Prompt.fontSize * (0.9f + i * 1.3f);
            Hud.Label(new Rect(0f, y, Screen.width, Hud.Prompt.fontSize * 1.6f),
                      "[" + options[i].key + "]  " + options[i].label, Hud.Prompt);
        }

        if (carried != null)
        {
            Hud.Label(new Rect(14f, Screen.height - Hud.LineHeight - 12f, 700f, Hud.LineHeight),
                      "Carrying: " + carried.itemName + "   [" + dropKey + "] drop",
                      Hud.Readout, new Color(1f, 0.92f, 0.6f));
        }
    }
}
