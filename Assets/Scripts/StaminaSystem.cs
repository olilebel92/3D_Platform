using UnityEngine;
using UnityEngine.UI;

public class StaminaSystem : MonoBehaviour
{
    [Header("Stamina Settings")]
    public float maxStamina = 100f;
    public float drainRate = 20f;
    public float rechargeRate = 10f;
    public float rechargeDelay = 1.5f;
    public float minStaminaToSprint = 20f;

    [Header("UI")]
    public Image staminaBarFill;
    public GameObject staminaBarPanel;

    private float currentStamina;
    private float rechargeTimer = 0f;
    private bool isExhausted = false;
    private bool isSprinting = false;

    void Start()
    {
        currentStamina = maxStamina;
        UpdateUI();
        ShowBar();
    }

    public void SetSprinting(bool sprinting)
    {
        isSprinting = sprinting;
       
    }

    public bool CanSprint()
    {
        return !isExhausted && currentStamina > 0f;
    }

    void Update()
    {
        if (isSprinting)
        {
            rechargeTimer = rechargeDelay;
            currentStamina -= drainRate * Time.deltaTime;
            currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);

            if (currentStamina <= 0f)
                isExhausted = true;

            ShowBar();
            UpdateUI();
        }
        else
        {
            if (rechargeTimer > 0f)
            {
                rechargeTimer -= Time.deltaTime;
                return;
            }

            currentStamina += rechargeRate * Time.deltaTime;
            currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);

            if (isExhausted && currentStamina >= minStaminaToSprint)
                isExhausted = false;

            UpdateUI();
        }
    }

    void ShowBar()
    {
        if (staminaBarPanel != null && !staminaBarPanel.activeSelf)
            staminaBarPanel.SetActive(true);
    }

    /// <summary>
    /// Called by PlayerUILinker after it wires the UI references at runtime.
    /// Forces an immediate bar refresh so the first frame shows the correct value.
    /// </summary>
    public void RefreshUI()
    {
        ShowBar();
        UpdateUI();
    }

    void UpdateUI()
    {
        if (staminaBarFill != null)
            staminaBarFill.fillAmount = currentStamina / maxStamina;
    }
}