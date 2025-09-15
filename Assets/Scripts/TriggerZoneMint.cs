using UnityEngine;

[RequireComponent(typeof(Collider))]
public class TriggerZoneMint : MonoBehaviour
{
    public CollectionManager manager;
    public int index;
    public string playerTag = "Player";

    private void Reset()
    {
        // Make sure the collider acts as a trigger
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        manager?.OnPlayerEnterZone(index);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        manager?.OnPlayerExitZone(index);
    }
}
