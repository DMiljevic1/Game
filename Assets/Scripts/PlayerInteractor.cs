using UnityEngine;

/// <summary>
/// The player's single point of contact with the world: looks down the camera,
/// picks the one thing in reach, shows its prompt, and routes the key press to it.
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
    public KeyCode interactKey = KeyCode.E;
    public KeyCode dropKey = KeyCode.G;

    [Header("Carrying")]
    [Tooltip("Where a carried item is parented. Usually a child of the camera.")]
    public Transform carrySocket;

    private Carryable carried;
    private IInteractable target;
    private string prompt;

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

        if (target != null && !string.IsNullOrEmpty(prompt) && Input.GetKeyDown(interactKey))
        {
            target.Interact(this);
        }

        if (carried != null && Input.GetKeyDown(dropKey))
        {
            DropCarried();
        }
    }

    private void AcquireTarget()
    {
        target = null;
        prompt = null;

        if (viewCamera == null) return;

        Ray ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, range, ~0, QueryTriggerInteraction.Collide)) return;

        // Never target ourselves or whatever we are already holding.
        if (hit.transform.root == transform.root) return;

        IInteractable candidate = hit.collider.GetComponentInParent<IInteractable>();
        if (candidate == null) return;

        string p = candidate.GetPrompt(this);
        if (string.IsNullOrEmpty(p)) return;

        target = candidate;
        prompt = p;
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
        GUI.Label(new Rect(Screen.width / 2f - 4f, Screen.height / 2f - 10f, 20f, 20f), "+");

        if (!string.IsNullOrEmpty(prompt))
        {
            GUI.Label(new Rect(Screen.width / 2f - 100f, Screen.height / 2f + 30f, 300f, 24f), prompt);
        }

        if (carried != null)
        {
            GUI.Label(new Rect(12f, Screen.height - 30f, 400f, 24f),
                      "Carrying: " + carried.itemName + "   (" + dropKey + " to drop)");
        }
    }
}
