using UnityEngine;

public class LootPickup : MonoBehaviour
{
    [Header("Pickup Settings")]
    public string playerTag = "Player";

    [Header("Audio (Optional)")]
    public AudioClip pickupSound;
    public float volume = 1f;

    private bool collected = false;

    void OnTriggerEnter(Collider other)
    {
        if (collected) return;
        if (!other.CompareTag(playerTag)) return;

        collected = true;

        if (pickupSound != null)
        {
            AudioSource.PlayClipAtPoint(
                pickupSound,
                transform.position,
                volume
            );
        }

        Destroy(gameObject);
    }
}
