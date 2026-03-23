using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealthSystem : MonoBehaviour
{
    // ─── Health Settings ──────────────────────────────────────────────────────
    [Header("Health Settings")]
    public int maxHealth = 5;
    public int currentHealth;

    // ─── UI ───────────────────────────────────────────────────────────────────
    [Header("UI (optional — leave blank for enemies)")]
    [Tooltip("Optional text label showing HP as numbers.")]
    public TMP_Text healthText;

    [Tooltip("Fill Image for the HP bar. Set Image Type to Filled, Fill Method to Horizontal.")]
    public Image hpBarFill;

    [Tooltip("Root panel of the HP bar — shown/hidden like the stamina bar.")]
    public GameObject hpBarPanel;

    // ─── Death Settings ───────────────────────────────────────────────────────
    [Header("Death Settings")]
    [Tooltip("If true, destroys the GameObject on death. Use for enemies.")]
    public bool destroyOnDeath = false;

    [Tooltip("Seconds before the object is destroyed after death.")]
    public float deathDelay = 0.5f;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    void Start()
    {
        currentHealth = maxHealth;
        UpdateHealthUI();
    }

    // ─── Public API ───────────────────────────────────────────────────────────
    public void TakeDamage(int amount)
    {
        currentHealth -= amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        UpdateHealthUI();

        Debug.Log(gameObject.name + " took " + amount + " damage! HP: " + currentHealth + "/" + maxHealth);

        // ── Damage Popup ──────────────────────────────────────────────────────
        if (DamagePopupManager.Instance != null)
        {
            bool isPlayer = CompareTag("Player");
            DamagePopupManager.Instance.ShowDamage(transform.position, amount, isPlayer);
        }

        if (currentHealth <= 0)
            Die();
    }

    public void Heal(int amount)
    {
        currentHealth += amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        UpdateHealthUI();

        Debug.Log(gameObject.name + " healed " + amount + " HP! HP: " + currentHealth + "/" + maxHealth);
    }

    // ─── Internal ─────────────────────────────────────────────────────────────
    void Die()
    {
        Debug.Log(gameObject.name + " died!");

        if (destroyOnDeath)
        {
            EnemyAI ai = GetComponent<EnemyAI>();
            if (ai != null) ai.enabled = false;

            Destroy(gameObject, deathDelay);
        }
    }

    void UpdateHealthUI()
    {
        if (healthText != null)
            healthText.text = "HP: " + currentHealth + "/" + maxHealth;

        if (hpBarFill != null)
            hpBarFill.fillAmount = (float)currentHealth / maxHealth;
    }
}