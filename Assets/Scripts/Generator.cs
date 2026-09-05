using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps monsters out of a radius while it is running, and burns fuel to do it.
/// The whole night loop hangs off this: fuel is finite, so "how far do we explore?"
/// becomes a real question, and running dry mid-night forces players outside.
///
/// Co-op note: fuel and running state are owned by the authority. Clients read them.
/// </summary>
[DisallowMultipleComponent]
public class Generator : MonoBehaviour, IInteractable
{
    // Every generator in the level, so monsters can ask "is this spot protected?"
    // without a scene search. Scene-object registry, not per-player state.
    private static readonly List<Generator> active = new List<Generator>();

    [Header("Fuel")]
    public float fuelCapacity = 100f;
    public float fuel = 100f;
    [Tooltip("Fuel burned per second of real time at timeScale 1.")]
    public float burnRate = 0.25f;
    [Tooltip("Fires OnLowFuel once when fuel drops below this.")]
    public float lowFuelThreshold = 25f;

    [Header("Protection")]
    [Tooltip("Monsters will not enter this radius while the generator runs.")]
    public float protectionRadius = 22f;

    [Header("Powered by this generator")]
    [Tooltip("Lights switched on while running. Makes protection visible at a glance.")]
    public Light[] poweredLights;

    [Header("State")]
    public bool isRunning = true;

    public event System.Action OnStarted = delegate { };
    public event System.Action OnStopped = delegate { };
    public event System.Action OnRanOutOfFuel = delegate { };
    public event System.Action OnLowFuel = delegate { };

    private bool lowFuelFired = false;

    public bool IsRunning { get { return isRunning; } }
    public bool HasFuel { get { return fuel > 0f; } }
    public float FuelNormalized { get { return fuelCapacity <= 0f ? 0f : Mathf.Clamp01(fuel / fuelCapacity); } }

    /// <summary>Real seconds of runtime left at the current burn rate.</summary>
    public float SecondsOfFuelLeft { get { return burnRate <= 0f ? Mathf.Infinity : fuel / burnRate; } }

    // Authority seam, same as TimeOfDay. Becomes IsServer once netcode is in.
    protected virtual bool HasAuthority { get { return true; } }

    /// <summary>True if any running generator covers this point.</summary>
    public static bool IsPointProtected(Vector3 point)
    {
        for (int i = 0; i < active.Count; i++)
        {
            Generator g = active[i];
            if (g == null || !g.isRunning) continue;
            if ((point - g.transform.position).sqrMagnitude <= g.protectionRadius * g.protectionRadius)
                return true;
        }
        return false;
    }

    void OnEnable() { active.Add(this); }
    void OnDisable() { active.Remove(this); }

    void Start()
    {
        ApplyPowerState();
    }

    void Update()
    {
        if (HasAuthority) TickFuel();
    }

    public string GetPrompt(PlayerInteractor interactor)
    {
        FuelCan can = interactor.GetCarried<FuelCan>();
        if (can != null && !can.IsEmpty)
        {
            return fuel >= fuelCapacity ? "Tank is full" : "Press E to refuel";
        }

        if (isRunning) return "Press E to switch off";
        return HasFuel ? "Press E to start" : "Out of fuel";
    }

    public void Interact(PlayerInteractor interactor)
    {
        // Pouring takes priority: if you walked here holding a can, that is why.
        FuelCan can = interactor.GetCarried<FuelCan>();
        if (can != null && !can.IsEmpty)
        {
            if (fuel >= fuelCapacity) return;
            can.PourInto(this);
            return;
        }

        if (isRunning) Stop();
        else Start_();
    }

    private void TickFuel()
    {
        if (!isRunning) return;

        // Scale with the clock so testing at timeScale 8 also drains 8x: a tank
        // always lasts the same fraction of a night regardless of test speed.
        float scale = 1f;
        TimeOfDay clock = TimeOfDay.Instance;
        if (clock != null)
        {
            if (clock.paused) return;
            scale = clock.timeScale;
        }

        fuel -= burnRate * Time.deltaTime * scale;

        if (!lowFuelFired && fuel <= lowFuelThreshold)
        {
            lowFuelFired = true;
            OnLowFuel();
        }

        if (fuel <= 0f)
        {
            fuel = 0f;
            Stop();
            OnRanOutOfFuel();
        }
    }

    public void Start_()
    {
        if (isRunning) return;
        if (!HasFuel) return;   // dry generator will not turn over

        isRunning = true;
        ApplyPowerState();
        OnStarted();
    }

    public void Stop()
    {
        if (!isRunning) return;

        isRunning = false;
        ApplyPowerState();
        OnStopped();
    }

    /// <summary>Pour fuel in. Returns how much actually fit.</summary>
    public float AddFuel(float amount)
    {
        if (amount <= 0f) return 0f;

        float before = fuel;
        fuel = Mathf.Min(fuelCapacity, fuel + amount);

        // Let the warning fire again next time it runs low.
        if (fuel > lowFuelThreshold) lowFuelFired = false;

        return fuel - before;
    }

    private void ApplyPowerState()
    {
        if (poweredLights == null) return;
        for (int i = 0; i < poweredLights.Length; i++)
        {
            if (poweredLights[i] != null) poweredLights[i].enabled = isRunning;
        }
    }

    void OnGUI()
    {
        // Temporary readout until there is real UI.
        string state = isRunning ? "RUNNING" : (HasFuel ? "OFF" : "OUT OF FUEL");
        GUI.Label(new Rect(12f, 52f, 420f, 22f),
                  string.Format("Generator: {0}   fuel {1:0}/{2:0}   ({3:0}s left)",
                                state, fuel, fuelCapacity, SecondsOfFuelLeft));
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = isRunning ? new Color(0.3f, 1f, 0.4f, 0.9f) : new Color(1f, 0.35f, 0.3f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, protectionRadius);
    }
}
